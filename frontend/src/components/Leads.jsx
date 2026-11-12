import { useCallback, useEffect, useState } from "react";
import { api } from "../api";
import { Badge, Empty, ErrorBanner, fmtDate } from "../ui";

const STAGES = ["New", "Contacted", "Qualified", "Won", "Lost"];

export default function Leads({ refreshKey, onChange, onOpenInquiry }) {
  const [stage, setStage] = useState("");
  const [rows, setRows] = useState(null);
  const [selectedId, setSelectedId] = useState(null);
  const [detail, setDetail] = useState(null);
  const [notes, setNotes] = useState("");
  const [days, setDays] = useState(2);
  const [busy, setBusy] = useState("");
  const [error, setError] = useState("");

  const loadList = useCallback(async () => {
    try {
      setRows(await api.leads({ stage }));
      setError("");
    } catch (e) {
      setError(e.message);
    }
  }, [stage]);

  const loadDetail = useCallback(async (id) => {
    if (!id) return setDetail(null);
    try {
      const lead = await api.lead(id);
      setDetail(lead);
      setNotes(lead.notes ?? "");
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

  async function run(label, fn) {
    setBusy(label);
    setError("");
    try {
      await fn();
      onChange();
    } catch (e) {
      setError(e.message);
    } finally {
      setBusy("");
    }
  }

  const changeStage = (id, newStage) => run("stage", () => api.updateLead(id, { stage: newStage }));
  const saveNotes = () => run("notes", () => api.updateLead(detail.id, { notes }));
  const draftFollowUp = () => run("followup", () => api.draftFollowUp(detail.id, Number(days)));
  const cancelFollowUp = (followUpId) => run("cancel", () => api.cancelFollowUp(detail.id, followUpId));

  return (
    <div className="split leads">
      <section className="list-pane wide">
        <div className="list-head">
          <select value={stage} onChange={(e) => setStage(e.target.value)} aria-label="Filter by stage">
            <option value="">All stages</option>
            {STAGES.map((s) => (
              <option key={s}>{s}</option>
            ))}
          </select>
        </div>
        <ErrorBanner message={error} onClose={() => setError("")} />
        {rows === null && <p className="muted pad">Loading…</p>}
        {rows?.length === 0 && <Empty>No leads yet. Sales inquiries are added here automatically after triage.</Empty>}

        {rows?.length > 0 && (
          <table className="table">
            <thead>
              <tr>
                <th>Lead</th>
                <th>Score</th>
                <th>Stage</th>
                <th>Next follow-up</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((l) => (
                <tr key={l.id} className={l.id === selectedId ? "selected" : ""} onClick={() => setSelectedId(l.id)}>
                  <td>
                    <strong>{l.name}</strong>
                    <div className="muted small">{l.email}</div>
                  </td>
                  <td>
                    <span className="score">{l.score}</span>
                  </td>
                  <td>
                    <select
                      value={l.stage}
                      onClick={(e) => e.stopPropagation()}
                      onChange={(e) => changeStage(l.id, e.target.value)}
                      aria-label={`Stage for ${l.name}`}
                      disabled={!!busy}
                    >
                      {STAGES.map((s) => (
                        <option key={s}>{s}</option>
                      ))}
                    </select>
                  </td>
                  <td className="small">{l.nextFollowUpAt ? fmtDate(l.nextFollowUpAt) : "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <section className="detail-pane">
        {!detail && <Empty>Select a lead to see its history and follow-ups.</Empty>}
        {detail && (
          <div className="stack">
            <div className="card">
              <div className="detail-head">
                <div>
                  <h2>{detail.name}</h2>
                  <p className="muted">
                    {detail.email} · via {detail.source} · added {fmtDate(detail.createdAt)}
                  </p>
                </div>
                <Badge value={detail.stage} />
              </div>
              <label>
                Notes
                <textarea rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} />
              </label>
              <div className="actions">
                <button onClick={saveNotes} disabled={!!busy || notes === (detail.notes ?? "")}>
                  Save notes
                </button>
              </div>
            </div>

            <div className="card">
              <h3>Follow-ups</h3>
              <p className="muted small">
                Pending follow-ups are emailed automatically by n8n once they're due. They stop when you mark the lead Won or Lost.
              </p>
              {detail.followUps.length === 0 && <p className="muted">None scheduled.</p>}
              <ul className="plain">
                {detail.followUps.map((f) => (
                  <li key={f.id} className="followup">
                    <div className="detail-head">
                      <div className="li-badges">
                        <Badge value={f.status} />
                        <span className="small muted">
                          {f.status === "Sent" ? `sent ${fmtDate(f.sentAt)}` : `due ${fmtDate(f.dueAt)}`}
                        </span>
                      </div>
                      {f.status === "Pending" && (
                        <button className="link" onClick={() => cancelFollowUp(f.id)} disabled={!!busy}>
                          Cancel
                        </button>
                      )}
                    </div>
                    <p className="message small">{f.message}</p>
                  </li>
                ))}
              </ul>
              <div className="actions">
                <label className="inline">
                  Send in
                  <input type="number" min="0" max="90" value={days} onChange={(e) => setDays(e.target.value)} /> days
                </label>
                <button onClick={draftFollowUp} disabled={!!busy}>
                  {busy === "followup" ? "Drafting…" : "Schedule AI follow-up"}
                </button>
              </div>
            </div>

            <div className="card">
              <h3>Inquiries</h3>
              <ul className="plain">
                {detail.inquiries.map((i) => (
                  <li key={i.id}>
                    <button className="link" onClick={() => onOpenInquiry(i.id)}>
                      {i.subject}
                    </button>
                    <span className="muted small"> · {fmtDate(i.receivedAt)}</span>
                  </li>
                ))}
              </ul>
            </div>
          </div>
        )}
      </section>
    </div>
  );
}
