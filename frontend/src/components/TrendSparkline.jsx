// Two-series weekly trend: total applications (neutral baseline, dashed) vs.
// positive responses (accent, solid) — line style differs in addition to color so
// identity never rests on color alone. Direct labels only on the last point (not
// every point), per the "selective direct labels" rule.
const W = 560;
const H = 120;
const PAD = 24;

export default function TrendSparkline({ points }) {
  const max = Math.max(1, ...points.map((p) => p.applied));
  const stepX = (W - PAD * 2) / Math.max(1, points.length - 1);
  const x = (i) => PAD + i * stepX;
  const y = (v) => H - PAD - (v / max) * (H - PAD * 2);

  const path = (key) =>
    points.map((p, i) => `${i === 0 ? "M" : "L"} ${x(i)} ${y(p[key])}`).join(" ");

  const last = points[points.length - 1];
  const lastI = points.length - 1;
  const hasData = points.some((p) => p.applied > 0);

  return (
    <div>
      <div style={{ display: "flex", gap: 18, marginBottom: 8, fontSize: 12, color: "var(--muted)" }}>
        <span style={{ display: "inline-flex", alignItems: "center", gap: 6 }}>
          <svg width="16" height="8" aria-hidden="true">
            <line x1="0" y1="4" x2="16" y2="4" stroke="var(--ink-soft)" strokeWidth="2" strokeDasharray="4 3" />
          </svg>
          Applied
        </span>
        <span style={{ display: "inline-flex", alignItems: "center", gap: 6 }}>
          <svg width="16" height="8" aria-hidden="true">
            <line x1="0" y1="4" x2="16" y2="4" stroke="var(--verified)" strokeWidth="2.5" />
          </svg>
          Positive responses
        </span>
      </div>

      {!hasData ? (
        <p className="muted" style={{ fontSize: 13 }}>No application activity in the last 8 weeks yet.</p>
      ) : (
        <svg viewBox={`0 0 ${W} ${H}`} width="100%" height={H} role="img" aria-label="Weekly applications and responses, last 8 weeks">
          <line x1={PAD} y1={H - PAD} x2={W - PAD} y2={H - PAD} stroke="var(--line)" strokeWidth="1" />

          <path d={path("applied")} fill="none" stroke="var(--ink-soft)" strokeWidth="2" strokeDasharray="4 3" />
          <path d={path("responses")} fill="none" stroke="var(--verified)" strokeWidth="2.5" />

          {points.map((p, i) => (
            <circle key={i} cx={x(i)} cy={y(p.responses)} r="3" fill="var(--verified)">
              <title>{`Week of ${p.weekStart}: ${p.applied} applied, ${p.responses} responses`}</title>
            </circle>
          ))}

          <text x={x(lastI)} y={y(last.applied) - 8} fontSize="11" fill="var(--ink-soft)" textAnchor="end">
            {last.applied}
          </text>
          <text x={x(lastI)} y={y(last.responses) - 8} fontSize="11" fill="var(--verified)" textAnchor="end" fontWeight="600">
            {last.responses}
          </text>
        </svg>
      )}
    </div>
  );
}
