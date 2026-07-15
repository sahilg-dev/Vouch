import { useEffect, useState } from "react";
import { api } from "../lib/api.js";

export default function Insights({ candidate }) {
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api.insights(candidate.id).then(setData).finally(() => setLoading(false));
  }, [candidate.id]);

  if (loading) return <div className="empty"><span className="spinner" /> Crunching your outcomes…</div>;
  if (!data) return <div className="empty">No insights yet.</div>;

  const pct = (n) => `${Math.round(n * 100)}%`;
  const maxRate = Math.max(0.01, ...data.emphasisInsights.map((e) => e.responseRate));

  return (
    <div className="stack" style={{ marginTop: 8 }}>
      <div className="card panel">
        <span className="eyebrow">The outcome loop</span>
        <div className="stat" style={{ marginTop: 12 }}>
          <div>
            <div className="big">{data.totalApplications}</div>
            <div className="k">applications</div>
          </div>
          <div>
            <div className="big">{data.responses}</div>
            <div className="k">positive responses</div>
          </div>
          <div>
            <div className="big" style={{ color: "var(--verified)" }}>{pct(data.responseRate)}</div>
            <div className="k">response rate</div>
          </div>
        </div>
      </div>

      {data.emphasisInsights.length > 0 && (
        <div className="card panel">
          <span className="eyebrow">Which themes win responses for you</span>
          <div style={{ marginTop: 12 }}>
            {data.emphasisInsights.map((e) => (
              <div key={e.tag} className="tagrow">
                <span className="chip">{e.tag}</span>
                <div className="bar">
                  <span style={{ width: `${(e.responseRate / maxRate) * 100}%` }} />
                </div>
                <span className="mono" style={{ fontFamily: "var(--mono)", fontSize: 12, textAlign: "right" }}>
                  {pct(e.responseRate)}
                </span>
              </div>
            ))}
          </div>
          <p className="muted" style={{ fontSize: 12, marginTop: 12, marginBottom: 0 }}>
            Response rate = positive replies among applications that leaned on each theme.
          </p>
        </div>
      )}

      <div className="card panel">
        <span className="eyebrow">Recommendations</span>
        <ul style={{ marginTop: 12, marginBottom: 0, paddingLeft: 18 }}>
          {data.recommendations.map((r, i) => (
            <li key={i} style={{ fontSize: 14, lineHeight: 1.5, marginBottom: 8 }}>
              {r}
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
