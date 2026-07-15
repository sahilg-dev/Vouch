import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../lib/api.js";

export default function Auth({ mode, onAuthed }) {
  const isSignup = mode === "signup";
  const navigate = useNavigate();
  const [form, setForm] = useState({ email: "", password: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const set = (k) => (e) => setForm({ ...form, [k]: e.target.value });

  const submit = async (e) => {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const r = isSignup
        ? await api.signup(form.email, form.password)
        : await api.login(form.email, form.password);
      await onAuthed(r);
    } catch (e) {
      setError(e.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="hero">
      <span className="eyebrow">The honest job copilot</span>
      <h1>
        {isSignup ? (
          <>
            Build a resume you can <span className="accent">defend</span>.
          </>
        ) : (
          <>
            Welcome back — let's keep your <span className="accent">honesty ledger</span> going.
          </>
        )}
      </h1>

      <form className="card panel stack" style={{ marginTop: 28, maxWidth: 420 }} onSubmit={submit}>
        <span className="eyebrow">{isSignup ? "Create your account" : "Log in"}</span>
        <div>
          <label>Email</label>
          <input
            type="email"
            value={form.email}
            onChange={set("email")}
            placeholder="jane@example.com"
            required
          />
        </div>
        <div>
          <label>Password</label>
          <input
            type="password"
            value={form.password}
            onChange={set("password")}
            placeholder={isSignup ? "At least 8 characters" : "Your password"}
            minLength={isSignup ? 8 : undefined}
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
                <span className="spinner" /> {isSignup ? "Creating account…" : "Logging in…"}
              </>
            ) : isSignup ? (
              "Sign up"
            ) : (
              "Log in"
            )}
          </button>
        </div>
        <p className="muted" style={{ fontSize: 13, margin: 0 }}>
          {isSignup ? (
            <>
              Already have an account?{" "}
              <a href="#" onClick={(e) => { e.preventDefault(); navigate("/login"); }}>
                Log in
              </a>
            </>
          ) : (
            <>
              New to Vouch?{" "}
              <a href="#" onClick={(e) => { e.preventDefault(); navigate("/signup"); }}>
                Create an account
              </a>
            </>
          )}
        </p>
      </form>
    </section>
  );
}
