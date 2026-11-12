import { useCallback, useEffect, useState } from "react";
import { api } from "../api";
import { Badge, Empty, ErrorBanner, fmtDate } from "../ui";

const STATUS_FILTERS = ["", "New", "Triaged", "Replied", "Closed"];

export default function Inbox({ refreshKey, selectedId, onSelect, onChange }) {
  const [status, setStatus] = useState("");
  const [items, setItems] = useState(null);
  const [detail, setDetail] = useState(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState("");
  const [instruction, setInstruction] = useState("");
  const [draftBody, setDraftBody] = useState("");

  const loadList = useCallback(async () => {
    try {
      setItems(await api.inquiries({ status }));
      setError("");
    } catch (e) {
      setError(e.message);
    }
  }, [status]);

  const loadDetail = useCallback(async (id) => {
    if (!id) return setDetail(null);
    try {
      setDetail(await api.inquiry(id));
    } catch (e) {
      setError(e.message);
    }
  }, []);

  useEffect(() => {
    loadList();
  }, [loadList, refreshKey]);

  useEffect(() => {
    loadDetail(selectedId);
  }, [selectedId, loadDetail, refreshKey]);

  const draft = detail ? [...detail.replies].reverse().find((r) => r.status === "Draft") : null;
  const sent = detail ? detail.replies.filter((r) => r.status === "Sent") : [];

  useEffect(() => {
    setDraftBody(draft?.body ?? "");
  }, [draft?.id, draft?.body]);

  async function run(label, fn) {
    setBusy(label);
    setError("");
    try {
      await fn();
    } catch (e) {
      setError(e.message);
    } finally {
      setBusy("");
    }
  }

  const generateDraft = () =>
    run("draft", async () => {
      await api.draftReply(detail.id, instruction.trim() || null);
      setInstruction("");
      onChange();
    });

  const saveDraft = () =>
    run("save", async () => {
      await api.updateReply(draft.id, draftBody);
      onChange();
    });

  const sendDraft = () => {
    if (!window.confirm(`Send this reply to ${detail.customerEmail}?`)) return;
    run("send", async () => {
      if (draftBody.trim() !== draft.body) await api.updateReply(draft.id, draftBody);
      await api.sendReply(draft.id);
      onChange();
    });
  };

  const retriage = () =>
    run("triage", async () => {
      await api.retriage(detail.id);
      onChange();
    });

  const closeInquiry = () =>
    run("close", async () => {
      await api.setInquiryStatus(detail.id, "Closed");
      onChange();
    });

  return (
    <div className="split">
      <aside className="list-pane">
        <div className="list-head">
          <select value={status} onChange={(e) => setStatus(e.target.value)} aria-label="Filter by status">
            {STATUS_FILTERS.map((s) => (
              <option key={s} value={s}>
                {s || "All statuses"}
              </option>
            ))}
          </select>
        </div>
        {items === null && <p className="muted pad">Loading…</p>}
        {items?.length === 0 && <Empty>No inquiries here yet. Use “+ New inquiry” to add one.</Empty>}
        <ul className="list">
          {items?.map((i) => (
            <li key={i.id}>
              <button className={`list-item ${i.id === selectedId ? "selected" : ""}`} onClick={() => onSelect(i.id)}>
                <div className="li-top">
                  <strong>{i.customerName}</strong>
                  <span className="muted small">{fmtDate(i.receivedAt)}</span>
                </div>
                <div className="li-subject">{i.subject}</div>
                <div className="li-badges">
                  <Badge value={i.status} />
                  <Badge value={i.category} />
                  {i.urgency === "High" && <Badge value="High" label="Urgent" />}
                </div>
              </button>
            </li>
          ))}
        </ul>
      </aside>

      <section className="detail-pane">
        <ErrorBanner message={error} onClose={() => setError("")} />

        {!detail && <Empty>Select an inquiry to read it and draft a reply.</Empty>}

        {detail && (
          <div className="stack">
            <div className="card">
              <div className="detail-head">
                <div>
                  <h2>{detail.subject}</h2>
                  <p className="muted">
                    {detail.customerName} · {detail.customerEmail} · {detail.channel} · {fmtDate(detail.receivedAt)}
                  </p>
                </div>
                <div className="li-badges">
                  <Badge value={detail.status} />
                </div>
              </div>

              {detail.category ? (
                <div className="triage">
                  <div className="li-badges">
                    <Badge value={detail.category} />
                    <Badge value={detail.sentiment} label={`${detail.sentiment} tone`} />
                    <Badge value={detail.urgency} label={`${detail.urgency} urgency`} />
                    {detail.lead && <Badge value="Qualified" label={`Lead · score ${detail.lead.score}`} />}
                  </div>
                  {detail.summary && <p className="summary">{detail.summary}</p>}
                </div>
              ) : (
                <div className="triage">
                  <p className="muted">This inquiry hasn't been triaged yet (the AI may have been unavailable).</p>
                  <button onClick={retriage} disabled={!!busy}>
                    {busy === "triage" ? "Triaging…" : "Run triage"}
                  </button>
                </div>
              )}

              <p className="message">{detail.message}</p>
            </div>

            {sent.map((r) => (
              <div key={r.id} className="card sent">
                <div className="li-badges">
                  <Badge value="Sent" label={`Sent ${fmtDate(r.sentAt)}`} />
                </div>
                <p className="message">{r.body}</p>
              </div>
            ))}

            {detail.status !== "Closed" && (
              <div className="card">
                <h3>Reply</h3>
                {!draft && (
                  <>
                    <label>
                      Optional guidance for the AI
                      <input
                        value={instruction}
                        onChange={(e) => setInstruction(e.target.value)}
                        placeholder="e.g. offer a call on Thursday, keep it short"
                        maxLength={500}
                      />
                    </label>
                    <div className="actions">
                      <button className="primary" onClick={generateDraft} disabled={!!busy}>
                        {busy === "draft" ? "Drafting…" : "Draft reply with AI"}
                      </button>
                    </div>
                  </>
                )}

                {draft && (
                  <>
                    <p className="muted small">
                      This is a draft. Nothing is sent until you press Send. Check it over and edit as you like.
                    </p>
                    <textarea
                      rows={9}
                      value={draftBody}
                      onChange={(e) => setDraftBody(e.target.value)}
                      maxLength={4000}
                      aria-label="Reply draft"
                    />
                    <div className="actions">
                      <button onClick={generateDraft} disabled={!!busy} title="Creates a new draft">
                        {busy === "draft" ? "Drafting…" : "Regenerate"}
                      </button>
                      <button onClick={saveDraft} disabled={!!busy || draftBody === draft.body}>
                        Save
                      </button>
                      <button className="primary" onClick={sendDraft} disabled={!!busy || !draftBody.trim()}>
                        {busy === "send" ? "Sending…" : "Send reply"}
                      </button>
                    </div>
                  </>
                )}
              </div>
            )}

            {detail.status !== "Closed" && (
              <div>
                <button className="link" onClick={closeInquiry} disabled={!!busy}>
                  Close without replying
                </button>
              </div>
            )}
          </div>
        )}
      </section>
    </div>
  );
}
