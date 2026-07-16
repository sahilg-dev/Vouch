import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { api } from "../lib/api.js";

export default function ResetPassword() {
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const navigate = useNavigate();
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [done, setDone] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await api.resetPassword(token, password);
      setDone(true);
    } catch (e) {
      setError(e.message);
    } finally {
      setBusy(false);
    }
  };

  if (!token) {
    return (
      <section className="hero">
        <div className="card panel stack" style={{ marginTop: 28, maxWidth: 420 }}>
          <div className="ledger flag">
            <span className="dot" />
            This reset link is missing its token — request a new one from the login page.
          </div>
        </div>
      </section>
    );
  }

  return (
    <section className="hero">
      <span className="eyebrow">The honest job copilot</span>
      <h1>Choose a new password.</h1>

      <div className="card panel stack" style={{ marginTop: 28, maxWidth: 420 }}>
        {done ? (
          <>
            <div className="ledger ok">
              <span className="dot" />
              Password updated — you can log in now.
            </div>
            <div>
              <button className="btn" onClick={() => navigate("/login")}>
                Go to login
              </button>
            </div>
          </>
        ) : (
          <form onSubmit={submit} className="stack">
            <div>
              <label>New password</label>
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="At least 8 characters"
                minLength={8}
                required
              />
            </div>
            {error && (
              <div className="ledger flag">
                <span className="dot" />
                {error}
              </div>
            )}
            <div>
              <button className="btn" type="submit" disabled={busy}>
                {busy ? (
                  <>
                    <span className="spinner" /> Updating…
                  </>
                ) : (
                  "Update password"
                )}
              </button>
            </div>
          </form>
        )}
      </div>
    </section>
  );
}
