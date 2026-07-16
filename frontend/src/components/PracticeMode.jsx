import { useEffect, useState } from "react";

const fmt = (secs) => `${Math.floor(secs / 60)}:${String(secs % 60).padStart(2, "0")}`;

export default function PracticeMode({ pack, onClose }) {
  const questions = pack.likelyQuestions ?? [];
  const gaps = pack.gapsToOwn ?? [];

  const [index, setIndex] = useState(0);
  const [revealed, setRevealed] = useState(false);
  const [phase, setPhase] = useState(questions.length > 0 ? "practice" : "recap");
  const [elapsed, setElapsed] = useState(0);

  useEffect(() => {
    if (phase !== "practice") return;
    const t = setInterval(() => setElapsed((e) => e + 1), 1000);
    return () => clearInterval(t);
  }, [phase]);

  const current = questions[index];
  const isLast = index === questions.length - 1;

  const next = () => {
    if (isLast) {
      setPhase(gaps.length > 0 ? "recap" : "done");
      return;
    }
    setIndex((i) => i + 1);
    setRevealed(false);
  };

  const prev = () => {
    setIndex((i) => Math.max(0, i - 1));
    setRevealed(false);
  };

  const restart = () => {
    setIndex(0);
    setRevealed(false);
    setElapsed(0);
    setPhase(questions.length > 0 ? "practice" : "recap");
  };

  return (
    <div className="overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()} style={{ maxWidth: 560 }}>
        <div className="modal-head">
          <div>
            <span className="eyebrow">Defense Pack practice</span>
            <h2 style={{ margin: "2px 0 0", fontSize: 18 }}>
              {phase === "practice" ? `Question ${index + 1} of ${questions.length}` : "Gaps to own honestly"}
            </h2>
          </div>
          <button className="btn ghost small" onClick={onClose}>
            Close
          </button>
        </div>

        <div className="panel">
          {phase === "practice" && current && (
            <div className="stack">
              <div className="stat" style={{ marginBottom: 0 }}>
                <div>
                  <div className="big" style={{ fontSize: 22 }}>{fmt(elapsed)}</div>
                  <div className="k">time elapsed</div>
                </div>
              </div>

              <div className="card panel" style={{ background: "var(--surface-2)" }}>
                <span className="eyebrow">Say your answer out loud, then reveal</span>
                <p style={{ fontSize: 17, lineHeight: 1.5, margin: "8px 0 0", fontWeight: 600 }}>
                  {current.question}
                </p>
              </div>

              {revealed ? (
                <div className="qa">
                  <div className="q">Truthful talking point</div>
                  <div className="a">
                    {current.truthfulTalkingPoint} <span className="fact-id">{current.sourceFactId}</span>
                  </div>
                </div>
              ) : (
                <div>
                  <button className="btn" onClick={() => setRevealed(true)}>
                    Reveal talking point
                  </button>
                </div>
              )}

              <div style={{ display: "flex", gap: 10, justifyContent: "space-between" }}>
                <button className="btn ghost small" onClick={prev} disabled={index === 0}>
                  Previous
                </button>
                <button className="btn small" onClick={next}>
                  {isLast ? "Finish" : "Next question"}
                </button>
              </div>
            </div>
          )}

          {phase === "recap" && (
            <div className="stack">
              <p className="muted" style={{ fontSize: 13, margin: 0 }}>
                Practiced {questions.length} question{questions.length === 1 ? "" : "s"} in {fmt(elapsed)}.
                These are the gaps worth owning honestly rather than dodging.
              </p>
              {gaps.map((g, i) => (
                <div key={i} className="qa" style={{ borderColor: "var(--gap-soft)" }}>
                  <div className="q" style={{ color: "var(--gap)" }}>{g.gap}</div>
                  <div className="a">{g.howToFrameIt}</div>
                </div>
              ))}
              <div style={{ display: "flex", gap: 10 }}>
                <button className="btn ghost small" onClick={restart}>
                  Practice again
                </button>
                <button className="btn small" onClick={onClose}>
                  Done
                </button>
              </div>
            </div>
          )}

          {phase === "done" && (
            <div className="stack">
              <p className="lead" style={{ margin: 0 }}>
                Nice work — {questions.length} question{questions.length === 1 ? "" : "s"} in {fmt(elapsed)}.
              </p>
              <div style={{ display: "flex", gap: 10 }}>
                <button className="btn ghost small" onClick={restart}>
                  Practice again
                </button>
                <button className="btn small" onClick={onClose}>
                  Done
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
