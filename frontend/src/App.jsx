import { useEffect, useState } from "react";
import { api } from "./lib/api.js";
import Setup from "./views/Setup.jsx";
import Discover from "./views/Discover.jsx";
import Tracker from "./views/Tracker.jsx";
import Insights from "./views/Insights.jsx";

const KEY = "vouch.candidateId";

export default function App() {
  const [candidate, setCandidate] = useState(null);
  const [view, setView] = useState("setup");
  const [toast, setToast] = useState(null);
  const [loading, setLoading] = useState(true);

  const flash = (msg) => {
    setToast(msg);
    setTimeout(() => setToast(null), 2600);
  };

  useEffect(() => {
    const id = localStorage.getItem(KEY);
    if (!id) return setLoading(false);
    api
      .getCandidate(id)
      .then((c) => {
        setCandidate(c);
        setView("discover");
      })
      .catch(() => localStorage.removeItem(KEY))
      .finally(() => setLoading(false));
  }, []);

  const onCandidate = (c) => {
    localStorage.setItem(KEY, c.id);
    setCandidate(c);
    setView("discover");
    flash("Profile ready — fact base extracted.");
  };

  const reset = () => {
    localStorage.removeItem(KEY);
    setCandidate(null);
    setView("setup");
  };

  return (
    <div className="shell">
      <div className="topbar">
        <div className="brand">
          <span className="mark">Vouch</span>
          <span className="tag">apply with a resume you can defend</span>
        </div>
        <nav className="nav">
          <button className={view === "setup" ? "active" : ""} onClick={() => setView("setup")}>
            Profile
          </button>
          <button
            className={view === "discover" ? "active" : ""}
            disabled={!candidate}
            onClick={() => setView("discover")}
          >
            Discover
          </button>
          <button
            className={view === "tracker" ? "active" : ""}
            disabled={!candidate}
            onClick={() => setView("tracker")}
          >
            Tracker
          </button>
          <button
            className={view === "insights" ? "active" : ""}
            disabled={!candidate}
            onClick={() => setView("insights")}
          >
            Insights
          </button>
        </nav>
      </div>

      {loading ? (
        <div className="empty">Loading…</div>
      ) : view === "setup" ? (
        <Setup candidate={candidate} onCandidate={onCandidate} onReset={reset} />
      ) : view === "discover" ? (
        <Discover candidate={candidate} flash={flash} />
      ) : view === "tracker" ? (
        <Tracker candidate={candidate} flash={flash} />
      ) : (
        <Insights candidate={candidate} />
      )}

      {toast && <div className="toast">{toast}</div>}
    </div>
  );
}
