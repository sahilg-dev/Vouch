import { useEffect, useState } from "react";
import { api } from "../lib/api.js";

const STATUSES = [
  "Saved", "Tailored", "ReadyToApply", "Applied",
  "Screening", "Interview", "Offer", "Rejected", "Withdrawn",
];

const LANES = [
  { name: "Queued", set: ["Saved", "Tailored", "ReadyToApply"] },
  { name: "Applied", set: ["Applied"] },
  { name: "Interviewing", set: ["Screening", "Interview"] },
  { name: "Decided", set: ["Offer", "Rejected", "Withdrawn"] },
];

export default function Tracker({ candidate, flash }) {
  const [apps, setApps] = useState([]);
  const [loading, setLoading] = useState(true);

  const load = async () => {
    setLoading(true);
    setApps(await api.applications(candidate.id));
    setLoading(false);
  };

  useEffect(() => {
    load();
  }, [candidate.id]);

  const move = async (id, status) => {
    await api.updateApplication(id, { status });
    flash(`Moved to ${status}.`);
    load();
  };

  if (loading) return <div className="empty"><span className="spinner" /> Loading tracker…</div>;
  if (apps.length === 0)
    return (
      <div className="card empty" style={{ marginTop: 8 }}>
        Nothing tracked yet. Apply to a role from Discover and it lands here.
      </div>
    );

  return (
    <div className="board" style={{ marginTop: 8 }}>
      {LANES.map((lane) => {
        const items = apps.filter((a) => lane.set.includes(a.status));
        return (
          <div key={lane.name} className="lane">
            <h4>
              {lane.name} · {items.length}
            </h4>
            {items.map((a) => (
              <div key={a.id} className="appcard">
                <div className="t">{a.title}</div>
                <div className="c">{a.company}</div>
                <select value={a.status} onChange={(e) => move(a.id, e.target.value)}>
                  {STATUSES.map((s) => (
                    <option key={s} value={s}>
                      {s}
                    </option>
                  ))}
                </select>
              </div>
            ))}
          </div>
        );
      })}
    </div>
  );
}
