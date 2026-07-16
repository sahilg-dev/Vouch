import { useEffect, useState } from "react";
import { BrowserRouter, Navigate, NavLink, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { api, getToken } from "./lib/api.js";
import Auth from "./views/Auth.jsx";
import ResetPassword from "./views/ResetPassword.jsx";
import VerifyEmail from "./views/VerifyEmail.jsx";
import Setup from "./views/Setup.jsx";
import Discover from "./views/Discover.jsx";
import Tracker from "./views/Tracker.jsx";
import Insights from "./views/Insights.jsx";

const CANDIDATE_KEY = "vouch.candidateId";

function RequireAuth({ authed, loading, children }) {
  if (loading) return <div className="empty">Loading…</div>;
  if (!authed) return <Navigate to="/login" replace />;
  return children;
}

export default function App() {
  const [account, setAccount] = useState(null); // { accountId, email, candidates }
  const [candidate, setCandidate] = useState(null);
  const [toast, setToast] = useState(null);
  const [loading, setLoading] = useState(true);

  const flash = (msg) => {
    setToast(msg);
    setTimeout(() => setToast(null), 2600);
  };

  const hydrateFromMe = async () => {
    const me = await api.me();
    setAccount(me);
    const storedId = localStorage.getItem(CANDIDATE_KEY);
    const active =
      me.candidates.find((c) => c.id === storedId) ?? me.candidates[me.candidates.length - 1] ?? null;
    setCandidate(active ?? null);
    if (active) localStorage.setItem(CANDIDATE_KEY, active.id);
    else localStorage.removeItem(CANDIDATE_KEY);
  };

  useEffect(() => {
    if (!getToken()) {
      setLoading(false);
      return;
    }
    hydrateFromMe()
      .catch(() => {
        setAccount(null);
        setCandidate(null);
      })
      .finally(() => setLoading(false));
  }, []);

  const onAuthed = async () => {
    await hydrateFromMe();
  };

  const onCandidate = (c) => {
    localStorage.setItem(CANDIDATE_KEY, c.id);
    setCandidate(c);
    setAccount((a) => (a ? { ...a, candidates: [...a.candidates, c] } : a));
    flash("Profile ready — fact base extracted.");
  };

  const switchCandidate = (id) => {
    const c = account?.candidates.find((c) => c.id === id);
    if (!c) return;
    localStorage.setItem(CANDIDATE_KEY, c.id);
    setCandidate(c);
  };

  const logout = () => {
    api.logout();
    localStorage.removeItem(CANDIDATE_KEY);
    setAccount(null);
    setCandidate(null);
  };

  const authed = !!account;

  const markVerified = () => setAccount((a) => (a ? { ...a, emailVerified: true } : a));

  return (
    <BrowserRouter>
      <Shell
        account={account}
        candidate={candidate}
        authed={authed}
        loading={loading}
        toast={toast}
        flash={flash}
        onAuthed={onAuthed}
        onCandidate={onCandidate}
        switchCandidate={switchCandidate}
        logout={logout}
        markVerified={markVerified}
      />
    </BrowserRouter>
  );
}

function Shell({ account, candidate, authed, loading, toast, flash, onAuthed, onCandidate, switchCandidate, logout, markVerified }) {
  const navigate = useNavigate();
  const location = useLocation();
  const [bannerDismissed, setBannerDismissed] = useState(false);
  const [newMatchCount, setNewMatchCount] = useState(0);

  // Re-check on every route change: leaving Discover marks matches viewed there, so
  // navigating away is the natural moment to reflect the badge clearing to 0, and
  // landing anywhere else (e.g. back from a saved-search alert) picks up new counts.
  useEffect(() => {
    if (!candidate) return;
    api.newMatchCount(candidate.id).then((r) => setNewMatchCount(r.count)).catch(() => {});
  }, [candidate?.id, location.pathname]);

  const handleAuthed = async (r) => {
    await onAuthed(r);
    navigate("/");
  };

  const handleLogout = () => {
    logout();
    navigate("/login");
  };

  const resendVerification = async () => {
    try {
      const r = await api.resendVerification();
      flash(r.devLink ? `${r.message} (dev: check server logs or open the link)` : r.message);
    } catch (e) {
      flash(`Couldn't resend: ${e.message}`);
    }
  };

  return (
    <div className="shell">
      {authed && account.emailVerified === false && !bannerDismissed && (
        <div className="ledger flag" style={{ borderRadius: 0, justifyContent: "center" }}>
          <span className="dot" />
          Verify your email to secure your account.
          <button className="btn ghost small" style={{ marginLeft: 10 }} onClick={resendVerification}>
            Resend link
          </button>
          <button className="btn ghost small" onClick={() => setBannerDismissed(true)}>
            Dismiss
          </button>
        </div>
      )}
      <div className="topbar">
        <div className="brand">
          <span className="mark">Vouch</span>
          <span className="tag">apply with a resume you can defend</span>
        </div>
        {authed && (
          <nav className="nav">
            <NavLink to="/" end className={({ isActive }) => (isActive ? "active" : "")}>
              Dashboard
            </NavLink>
            <NavLink to="/setup" className={({ isActive }) => (isActive ? "active" : "")}>
              Profile
            </NavLink>
            <NavLink
              to="/discover"
              className={({ isActive }) => (isActive ? "active" : "")}
              onClick={(e) => !candidate && e.preventDefault()}
              style={{ opacity: candidate ? 1 : 0.4, pointerEvents: candidate ? "auto" : "none" }}
            >
              Discover
              {newMatchCount > 0 && (
                <span
                  className="chip"
                  style={{ marginLeft: 6, background: "var(--verified)", color: "#fff", borderColor: "var(--verified)" }}
                >
                  {newMatchCount}
                </span>
              )}
            </NavLink>
            <NavLink
              to="/tracker"
              className={({ isActive }) => (isActive ? "active" : "")}
              onClick={(e) => !candidate && e.preventDefault()}
              style={{ opacity: candidate ? 1 : 0.4, pointerEvents: candidate ? "auto" : "none" }}
            >
              Tracker
            </NavLink>
          </nav>
        )}
        {authed && (
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            {account.candidates.length > 1 && (
              <select value={candidate?.id ?? ""} onChange={(e) => switchCandidate(e.target.value)}>
                {account.candidates.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.fullName}
                  </option>
                ))}
              </select>
            )}
            <button className="btn ghost small" onClick={handleLogout}>
              Log out
            </button>
          </div>
        )}
      </div>

      <Routes>
        <Route path="/login" element={authed ? <Navigate to="/" replace /> : <Auth mode="login" onAuthed={handleAuthed} />} />
        <Route path="/signup" element={authed ? <Navigate to="/" replace /> : <Auth mode="signup" onAuthed={handleAuthed} />} />
        <Route path="/reset-password" element={<ResetPassword />} />
        <Route path="/verify-email" element={<VerifyEmail onVerified={markVerified} />} />
        <Route
          path="/"
          element={
            <RequireAuth authed={authed} loading={loading}>
              <Insights candidate={candidate} />
            </RequireAuth>
          }
        />
        <Route
          path="/setup"
          element={
            <RequireAuth authed={authed} loading={loading}>
              <Setup candidate={candidate} onCandidate={onCandidate} />
            </RequireAuth>
          }
        />
        <Route
          path="/discover"
          element={
            <RequireAuth authed={authed} loading={loading}>
              {candidate ? <Discover candidate={candidate} flash={flash} /> : <Navigate to="/setup" replace />}
            </RequireAuth>
          }
        />
        <Route
          path="/tracker"
          element={
            <RequireAuth authed={authed} loading={loading}>
              {candidate ? <Tracker candidate={candidate} flash={flash} /> : <Navigate to="/setup" replace />}
            </RequireAuth>
          }
        />
      </Routes>

      {toast && <div className="toast">{toast}</div>}
    </div>
  );
}
