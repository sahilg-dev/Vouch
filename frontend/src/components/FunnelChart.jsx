// Reuses the app's existing .tagrow/.bar pattern (already used for the emphasis-theme
// bars in Insights) so the funnel reads as part of the same visual system rather than
// a bolted-on chart.
export default function FunnelChart({ stages }) {
  const max = Math.max(1, ...stages.map((s) => s.count));
  return (
    <div>
      {stages.map((s) => (
        <div key={s.status} className="tagrow">
          <span className="chip">{splitCamel(s.status)}</span>
          <div className="bar">
            <span style={{ width: `${(s.count / max) * 100}%` }} />
          </div>
          <span className="mono" style={{ fontFamily: "var(--mono)", fontSize: 12, textAlign: "right" }}>
            {s.count}
          </span>
        </div>
      ))}
    </div>
  );
}

function splitCamel(s) {
  return s.replace(/([a-z])([A-Z])/g, "$1 $2");
}
