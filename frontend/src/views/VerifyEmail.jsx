import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../lib/api.js";

export default function VerifyEmail({ onVerified }) {
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const [status, setStatus] = useState("verifying"); // verifying | done | error
  const [error, setError] = useState(null);

  useEffect(() => {
    if (!token) {
      setStatus("error");
      setError("This verification link is missing its token.");
      return;
    }
    api
      .verifyEmail(token)
      .then(() => {
        setStatus("done");
        onVerified?.();
      })
      .catch((e) => {
        setStatus("error");
        setError(e.message);
      });
  }, [token]);

  return (
    <section className="hero">
      <span className="eyebrow">The honest job copilot</span>
      <h1>Email verification</h1>

      <div className="card panel stack" style={{ marginTop: 28, maxWidth: 420 }}>
        {status === "verifying" && (
          <p className="lead" style={{ margin: 0 }}>
            <span className="spinner" /> Verifying…
          </p>
        )}
        {status === "done" && (
          <>
            <div className="ledger ok">
              <span className="dot" />
              Your email is verified.
            </div>
            <div>
              <Link className="btn" to="/">
                Go to dashboard
              </Link>
            </div>
          </>
        )}
        {status === "error" && (
          <div className="ledger flag">
            <span className="dot" />
            {error}
          </div>
        )}
      </div>
    </section>
  );
}
