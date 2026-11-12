import { useEffect, useRef, useState } from "react";
import { api } from "../api";
import { ErrorBanner } from "../ui";

const MAX_LOGO_PX = 256;

/** Shrinks the chosen image in the browser so the stored logo stays small (and keeps transparency). */
function resizeLogo(file) {
  return new Promise((resolve, reject) => {
    if (!/^image\/(png|jpeg|webp|gif|bmp)$/.test(file.type)) {
      return reject(new Error("Please choose a PNG, JPEG or WebP image."));
    }
    const url = URL.createObjectURL(file);
    const img = new Image();
    img.onload = () => {
      URL.revokeObjectURL(url);
      const scale = Math.min(1, MAX_LOGO_PX / Math.max(img.width, img.height));
      const canvas = document.createElement("canvas");
      canvas.width = Math.max(1, Math.round(img.width * scale));
      canvas.height = Math.max(1, Math.round(img.height * scale));
      canvas.getContext("2d").drawImage(img, 0, 0, canvas.width, canvas.height);
      resolve(canvas.toDataURL("image/png"));
    };
    img.onerror = () => {
      URL.revokeObjectURL(url);
      reject(new Error("That file couldn't be read as an image."));
    };
    img.src = url;
  });
}

export default function Settings({ onSaved }) {
  const [form, setForm] = useState(null);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState("");
  const fileRef = useRef(null);

  useEffect(() => {
    api
      .settings()
      .then(setForm)
      .catch((e) => setError(e.message));
  }, []);

  const set = (key) => (e) => {
    setSaved(false);
    setForm((f) => ({ ...f, [key]: e.target.value }));
  };

  async function chooseLogo(e) {
    const file = e.target.files?.[0];
    e.target.value = ""; // lets you pick the same file again later
    if (!file) return;
    setError("");
    try {
      const dataUrl = await resizeLogo(file);
      setSaved(false);
      setForm((f) => ({ ...f, logoDataUrl: dataUrl }));
    } catch (err) {
      setError(err.message);
    }
  }

  const removeLogo = () => {
    setSaved(false);
    setForm((f) => ({ ...f, logoDataUrl: "" }));
  };

  async function save(e) {
    e.preventDefault();
    setSaving(true);
    setError("");
    try {
      setForm(await api.saveSettings({ ...form, logoDataUrl: form.logoDataUrl ?? "" }));
      setSaved(true);
      onSaved?.();
    } catch (err) {
      setError(err.message);
    } finally {
      setSaving(false);
    }
  }

  if (!form) return error ? <ErrorBanner message={error} /> : <p className="muted">Loading…</p>;

  return (
    <form className="card narrow" onSubmit={save}>
      <h2>Business profile</h2>
      <p className="muted">
        Your name and logo appear in the header, on the sign-in page and in the browser tab. The rest is everything the AI
        knows about your business. The better it is, the better the triage and the drafted replies.
      </p>
      <ErrorBanner message={error} />

      <div className="logo-row">
        <div className="logo-preview" aria-label="Logo preview">
          {form.logoDataUrl ? <img src={form.logoDataUrl} alt="Your logo" /> : <span className="muted small">No logo</span>}
        </div>
        <div>
          <input ref={fileRef} type="file" accept="image/png,image/jpeg,image/webp" hidden onChange={chooseLogo} aria-label="Logo file" />
          <div className="actions left">
            <button type="button" onClick={() => fileRef.current?.click()}>
              {form.logoDataUrl ? "Change logo" : "Upload logo"}
            </button>
            {form.logoDataUrl && (
              <button type="button" className="link" onClick={removeLogo}>
                Remove
              </button>
            )}
          </div>
          <p className="muted small">PNG, JPEG or WebP. It's resized automatically. Click Save to apply.</p>
        </div>
      </div>

      <label>
        Business name
        <input required value={form.name} onChange={set("name")} maxLength={200} />
      </label>
      <label>
        What you do
        <textarea rows={3} value={form.description} onChange={set("description")} maxLength={2000} />
      </label>
      <label>
        Tone of voice
        <input value={form.tone} onChange={set("tone")} maxLength={100} placeholder="Friendly and professional" />
      </label>
      <label>
        FAQ, prices and policies
        <textarea
          rows={8}
          value={form.faq}
          onChange={set("faq")}
          maxLength={4000}
          placeholder="Opening hours, prices, delivery, refund policy… The AI only states facts that appear here."
        />
      </label>

      <div className="actions">
        {saved && <span className="muted">Saved ✓</span>}
        <button className="primary" type="submit" disabled={saving}>
          {saving ? "Saving…" : "Save profile"}
        </button>
      </div>
    </form>
  );
}
