const TONES = {
  High: "red", Medium: "amber", Low: "gray",
  Negative: "red", Neutral: "gray", Positive: "green",
  New: "blue", Triaged: "amber", Replied: "green", Closed: "gray",
  Sales: "green", Support: "blue", Billing: "amber", Partnership: "purple", Spam: "red", Other: "gray",
  Contacted: "blue", Qualified: "purple", Won: "green", Lost: "gray",
  Pending: "amber", Sent: "green", Cancelled: "gray", Draft: "amber",
};

export function Badge({ value, label }) {
  if (!value) return null;
  return <span className={`badge ${TONES[value] ?? "gray"}`}>{label ?? value}</span>;
}

export function ErrorBanner({ message, onClose }) {
  if (!message) return null;
  return (
    <div className="error" role="alert">
      <span>{message}</span>
      {onClose && (
        <button className="link" onClick={onClose} aria-label="Dismiss">
          Dismiss
        </button>
      )}
    </div>
  );
}

export function Empty({ children }) {
  return <div className="empty">{children}</div>;
}

export const fmtDate = (s) =>
  s ? new Date(s).toLocaleString([], { dateStyle: "medium", timeStyle: "short" }) : "—";

export const fmtDay = (s) => new Date(`${s}T00:00:00`).toLocaleDateString([], { weekday: "short" });
