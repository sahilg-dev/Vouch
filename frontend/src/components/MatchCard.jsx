import { scoreColor } from "../lib/format.js";

function timeAgo(iso) {
  if (!iso) return "date n/a";
  const d = (Date.now() - new Date(iso).getTime()) / 86400000;
  if (d < 1) return "today";
  if (d < 2) return "yesterday";
  if (d < 30) return `${Math.floor(d)}d ago`;
  return `${Math.floor(d / 30)}mo ago`;
}

export default function MatchCard({ match, onTailor, onApply }) {
  return (
    <div className="card job">
      <div className="score" style={{ background: scoreColor(match.score) }}>
        {match.score}
        <span className="lbl">FIT</span>
      </div>

      <div>
        <h3>{match.title}</h3>
        <div className="meta">
          <span>{match.company}</span>
          {match.location && <span>· {match.location}</span>}
          <span>· {timeAgo(match.postedAt)}</span>
          {match.isRemote && <span className="chip remote">remote</span>}
        </div>
        <p className="headline">{match.headline}</p>

        <div className="evidence">
          <div className="col fit">
            <h4>Why it fits</h4>
            <ul>
              {match.strengths.slice(0, 3).map((s, i) => (
                <li key={i}>{s}</li>
              ))}
            </ul>
          </div>
          <div className="col gap">
            <h4>Watch-outs</h4>
            <ul>
              {match.gaps.length ? (
                match.gaps.slice(0, 3).map((g, i) => <li key={i}>{g}</li>)
              ) : (
                <li className="muted">No notable gaps</li>
              )}
            </ul>
          </div>
        </div>
      </div>

      <div className="actions">
        <button className="btn small" onClick={() => onTailor(match)}>
          Tailor resume
        </button>
        <button className="btn secondary small" onClick={() => onApply(match)}>
          Review &amp; apply
        </button>
        <a className="btn ghost small" href={match.applyUrl} target="_blank" rel="noreferrer"
           style={{ textAlign: "center", textDecoration: "none" }}>
          View posting
        </a>
      </div>
    </div>
  );
}
