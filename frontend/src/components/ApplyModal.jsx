import { useEffect, useState } from "react";
import { api } from "../lib/api.js";

export default function ApplyModal({ candidate, match, tailoredResumeId, onClose, flash }) {
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    api.prefill(candidate.id, match.jobId).then(setData).catch((e) => setError(e.message));
  }, [candidate.id, match.jobId]);

  const logApplied = async () => {
    setSaving(true);
    try {
      const appRec = await api.createApplication({
        candidateId: candidate.id,
        jobId: match.jobId,
        tailoredResumeId: tailoredResumeId ?? null,
      });
      await api.updateApplication(appRec.id, { status: "Applied" });
      flash("Logged as applied — track it in Tracker.");
      onClose();
    } catch (e) {
      setError(e.message);
    } finally {
      setSaving(false);
    }
  };

  const copy = (text) => navigator.clipboard?.writeText(text);

  return (
    <div className="overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <div>
            <span className="eyebrow">Review &amp; apply</span>
            <h2 style={{ margin: "2px 0 0", fontSize: 18 }}>
              {match.title} · {match.company}
            </h2>
          </div>
          <button className="btn ghost small" onClick={onClose}>
            Close
          </button>
        </div>

        <div className="panel stack">
          <p className="muted" style={{ fontSize: 13, marginTop: 0 }}>
            Vouch never submits for you. Review what you'll send, open the real posting, paste it
            in, and submit yourself — then log it here so the outcome loop can learn.
          </p>

          {error && <div className="ledger flag"><span className="dot" />{error}</div>}
          {!data && !error && <div className="empty"><span className="spinner" /> Preparing…</div>}

          {data && (
            <>
              <div className="card panel" style={{ background: "var(--surface-2)" }}>
                <span className="eyebrow">Your details</span>
                <div style={{ marginTop: 8 }}>
                  {Object.entries(data.fields).map(([k, v]) => (
                    <div key={k} style={{ fontSize: 13, padding: "3px 0" }}>
                      <strong>{k}:</strong> {v || <span className="muted">—</span>}
                    </div>
                  ))}
                </div>
              </div>

              {data.coverNote && (
                <div className="card panel" style={{ background: "var(--surface-2)" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                    <span className="eyebrow">Cover note</span>
                    <button className="btn ghost small" onClick={() => copy(data.coverNote)}>Copy</button>
                  </div>
                  <p style={{ fontSize: 14, lineHeight: 1.55, marginBottom: 0 }}>{data.coverNote}</p>
                </div>
              )}

              <div className="card panel" style={{ background: "var(--surface-2)" }}>
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                  <span className="eyebrow">Resume to paste</span>
                  <button className="btn ghost small" onClick={() => copy(data.resumePlainText)}>Copy</button>
                </div>
                <pre style={{ whiteSpace: "pre-wrap", fontFamily: "var(--mono)", fontSize: 12,
                  lineHeight: 1.5, margin: "8px 0 0", color: "var(--ink-soft)" }}>
                  {data.resumePlainText}
                </pre>
              </div>

              <div style={{ display: "flex", gap: 10 }}>
                <a className="btn" href={data.applyUrl} target="_blank" rel="noreferrer"
                   style={{ textDecoration: "none" }}>
                  Open application ↗
                </a>
                <button className="btn secondary" onClick={logApplied} disabled={saving}>
                  {saving ? "Saving…" : "Mark as applied"}
                </button>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
