import { useEffect, useRef, useState } from "react";
import { api } from "../lib/api.js";

export default function TailoredModal({ candidate, match, onClose, onTailored, flash }) {
  const [tab, setTab] = useState("resume");
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);
  const started = useRef(false);

  useEffect(() => {
    if (started.current) return; // avoid duplicate (paid) calls in StrictMode
    started.current = true;
    api
      .tailor(candidate.id, match.jobId)
      .then((d) => {
        setData(d);
        onTailored?.(d.tailoredResumeId);
      })
      .catch((e) => setError(e.message));
  }, [candidate.id, match.jobId]);

  return (
    <div className="overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <div>
            <span className="eyebrow">Tailored for</span>
            <h2 style={{ margin: "2px 0 0", fontSize: 18 }}>
              {match.title} · {match.company}
            </h2>
          </div>
          <button className="btn ghost small" onClick={onClose}>
            Close
          </button>
        </div>

        {error && (
          <div className="panel">
            <div className="ledger flag">
              <span className="dot" />
              Couldn't tailor: {error}
            </div>
          </div>
        )}

        {!data && !error && (
          <div className="empty">
            <span className="spinner" /> Tailoring honestly, checking every claim, and building
            your defense pack…
          </div>
        )}

        {data && (
          <>
            <div className="panel" style={{ paddingBottom: 0 }}>
              {data.validation.allSupported ? (
                <div className="ledger ok">
                  <span className="dot" />
                  Honesty Ledger: every line traces to a fact in your resume.
                </div>
              ) : (
                <div className="ledger flag">
                  <span className="dot" />
                  {data.validation.unsupportedClaims.length} line(s) flagged as unsupported — review
                  before using.
                </div>
              )}
              {data.emphasisTags?.length > 0 && (
                <p className="muted" style={{ fontSize: 12, marginTop: 10 }}>
                  Leaning on:{" "}
                  {data.emphasisTags.map((t) => (
                    <span key={t} className="chip" style={{ marginRight: 6 }}>
                      {t}
                    </span>
                  ))}
                </p>
              )}
            </div>

            <div className="tabs">
              <button className={tab === "resume" ? "active" : ""} onClick={() => setTab("resume")}>
                Resume
              </button>
              <button className={tab === "diff" ? "active" : ""} onClick={() => setTab("diff")}>
                What changed ({data.diff.length})
              </button>
              <button className={tab === "defense" ? "active" : ""} onClick={() => setTab("defense")}>
                Defense Pack
              </button>
            </div>

            <div className="panel">
              {tab === "resume" && <ResumeTab content={data.content} cover={data.coverNote} />}
              {tab === "diff" && <DiffTab diff={data.diff} flags={data.validation.unsupportedClaims} />}
              {tab === "defense" && <DefenseTab pack={data.defensePack} />}
            </div>
          </>
        )}
      </div>
    </div>
  );
}

function ResumeTab({ content, cover }) {
  return (
    <div className="stack">
      <p className="lead">{content.summary}</p>
      {content.sections.map((s, i) => (
        <div key={i}>
          <h3 style={{ fontSize: 15, margin: "0 0 8px" }}>{s.heading}</h3>
          <ul style={{ margin: 0, paddingLeft: 18 }}>
            {s.bullets.map((b, j) => (
              <li key={j} style={{ marginBottom: 6, fontSize: 14, lineHeight: 1.5 }}>
                {b.text} <span className="fact-id">{b.sourceFactId}</span>
              </li>
            ))}
          </ul>
        </div>
      ))}
      {cover && (
        <div className="card panel" style={{ background: "var(--surface-2)" }}>
          <span className="eyebrow">Cover note</span>
          <p style={{ fontSize: 14, lineHeight: 1.55, marginBottom: 0 }}>{cover}</p>
        </div>
      )}
    </div>
  );
}

function DiffTab({ diff, flags }) {
  if (!diff.length) return <p className="muted">No changes recorded.</p>;
  return (
    <div>
      {flags?.length > 0 &&
        flags.map((f, i) => (
          <div key={`f${i}`} className="diff" style={{ borderColor: "var(--warn)" }}>
            <span className="change" style={{ color: "var(--warn)" }}>Flagged · unsupported</span>
            <div className="after">{f.text}</div>
            <div className="why">{f.why}</div>
          </div>
        ))}
      {diff.map((d, i) => (
        <div key={i} className="diff">
          <span className="change">
            {d.changeType} · <span className="fact-id">{d.sourceFactId}</span>
          </span>
          {d.before && <div className="before">{d.before}</div>}
          <div className="after">{d.after}</div>
          <div className="why">{d.reason}</div>
        </div>
      ))}
    </div>
  );
}

function DefenseTab({ pack }) {
  return (
    <div>
      <span className="eyebrow">Likely questions</span>
      <div style={{ marginTop: 10 }}>
        {pack.likelyQuestions.map((q, i) => (
          <div key={i} className="qa">
            <div className="q">{q.question}</div>
            <div className="a">
              {q.truthfulTalkingPoint} <span className="fact-id">{q.sourceFactId}</span>
            </div>
          </div>
        ))}
      </div>
      {pack.gapsToOwn?.length > 0 && (
        <>
          <span className="eyebrow" style={{ display: "block", marginTop: 18 }}>
            Gaps to own honestly
          </span>
          <div style={{ marginTop: 10 }}>
            {pack.gapsToOwn.map((g, i) => (
              <div key={i} className="qa" style={{ borderColor: "var(--gap-soft)" }}>
                <div className="q" style={{ color: "var(--gap)" }}>{g.gap}</div>
                <div className="a">{g.howToFrameIt}</div>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
