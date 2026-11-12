import { ErrorBanner, fmtDay } from "../ui";

function Tile({ label, value, hint }) {
  return (
    <div className="tile">
      <div className="tile-value">{value ?? "–"}</div>
      <div className="tile-label">{label}</div>
      {hint && <div className="tile-hint">{hint}</div>}
    </div>
  );
}

function BarList({ rows }) {
  const max = Math.max(1, ...rows.map((r) => r.count));
  if (rows.length === 0) return <p className="muted">Nothing to show yet.</p>;
  return (
    <ul className="bars">
      {rows.map((r) => (
        <li key={r.name}>
          <span className="bar-name">{r.name}</span>
          <span className="bar-track">
            <span className="bar-fill" style={{ width: `${(r.count / max) * 100}%` }} />
          </span>
          <span className="bar-count">{r.count}</span>
        </li>
      ))}
    </ul>
  );
}

export default function Dashboard({ stats, error }) {
  if (error) return <ErrorBanner message={error} />;
  if (!stats) return <p className="muted">Loading…</p>;

  const maxDaily = Math.max(1, ...stats.daily.map((d) => d.count));

  return (
    <div className="stack">
      <div className="tiles">
        <Tile label="Inquiries" value={stats.totalInquiries} />
        <Tile label="Needs a reply" value={stats.openInquiries} hint="New or triaged" />
        <Tile label="Leads" value={stats.totalLeads} />
        <Tile label="Follow-ups queued" value={stats.pendingFollowUps} hint="Sent automatically by n8n" />
      </div>

      <div className="grid-2">
        <section className="card">
          <h2>Last 7 days</h2>
          <div className="columns" role="img" aria-label="Inquiries per day over the last 7 days">
            {stats.daily.map((d) => (
              <div key={d.date} className="column">
                <span className="column-count">{d.count}</span>
                <span className="column-bar" style={{ height: `${(d.count / maxDaily) * 100}%` }} />
                <span className="column-label">{fmtDay(d.date)}</span>
              </div>
            ))}
          </div>
        </section>

        <section className="card">
          <h2>Lead pipeline</h2>
          <BarList rows={stats.byStage} />
        </section>
      </div>

      <section className="card">
        <h2>Inquiries by category</h2>
        <BarList rows={stats.byCategory} />
      </section>
    </div>
  );
}
