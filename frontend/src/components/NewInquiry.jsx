import { useState } from "react";
import { api } from "../api";
import { ErrorBanner } from "../ui";

const CHANNELS = ["Web", "Email", "Facebook", "Phone", "Walk-in"];

export default function NewInquiry({ onClose, onCreated }) {
  const [form, setForm] = useState({
    customerName: "",
    customerEmail: "",
    channel: "Web",
    subject: "",
    message: "",
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const set = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.value }));

  async function submit(e) {
    e.preventDefault();
    setSaving(true);
    setError("");
    try {
      onCreated(await api.createInquiry(form));
    } catch (err) {
      setError(err.message);
      setSaving(false);
    }
  }

  return (
    <div className="overlay" onClick={onClose}>
      <form className="modal" onClick={(e) => e.stopPropagation()} onSubmit={submit}>
        <h2>New inquiry</h2>
        <p className="muted">Log a message that came in. The AI will triage it and, if it looks like a sales lead, add it to your pipeline.</p>
        <ErrorBanner message={error} />

        <div className="row">
          <label>
            Customer name
            <input required value={form.customerName} onChange={set("customerName")} maxLength={200} />
          </label>
          <label>
            Email
            <input required type="email" value={form.customerEmail} onChange={set("customerEmail")} maxLength={320} />
          </label>
        </div>
        <div className="row">
          <label>
            Subject
            <input value={form.subject} onChange={set("subject")} maxLength={300} />
          </label>
          <label>
            Channel
            <select value={form.channel} onChange={set("channel")}>
              {CHANNELS.map((c) => (
                <option key={c}>{c}</option>
              ))}
            </select>
          </label>
        </div>
        <label>
          Message
          <textarea required rows={6} value={form.message} onChange={set("message")} maxLength={4000} />
        </label>

        <div className="actions">
          <button type="button" onClick={onClose} disabled={saving}>
            Cancel
          </button>
          <button className="primary" type="submit" disabled={saving}>
            {saving ? "Saving & triaging…" : "Add inquiry"}
          </button>
        </div>
      </form>
    </div>
  );
}
