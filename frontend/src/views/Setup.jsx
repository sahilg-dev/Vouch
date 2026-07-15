import { useState } from "react";
import { api } from "../lib/api.js";

export default function Setup({ candidate, onCandidate, onReset }) {
  const [form, setForm] = useState({
    fullName: "",
    email: "",
    phone: "",
    location: "",
    baseResumeText: "",
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const set = (k) => (e) => setForm({ ...form, [k]: e.target.value });

  const loadResumeFile = async (e) => {
    const file = e.target.files?.[0];
    if (!file) return;
    if (!file.name.match(/\.(txt|md|text)$/i)) {
      setError("For now, upload a .txt or .md resume file, or paste your resume text below.");
      return;
    }
    const text = await file.text();
    setForm((current) => ({ ...current, baseResumeText: text }));
  };

  const submit = async () => {
    setError(null);
    if (!form.fullName || !form.email || form.baseResumeText.length < 80) {
      setError("Add your name, email, and paste a real resume (at least a few lines).");
      return;
    }
    setBusy(true);
    try {
      const c = await api.createCandidate(form);
      onCandidate(c);
    } catch (e) {
      setError(e.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <section className="hero">
        <span className="eyebrow">The honest job copilot</span>
        <h1>
          Apply with a resume you can <span className="accent">defend</span> — and walk in
          with the hard questions already answered.
        </h1>
        <p className="lead">
          Vouch tailors your resume to each role using only facts you actually have, proves
          every line traces back to one, and hands you the interview defense for what it
          emphasized. No spray-and-pray. No claims you can't back up.
        </p>
        <div className="pillars">
          <div className="pillar card">
            <span className="n">01</span>
            <h3>Honesty Ledger</h3>
            <p>Every tailored line is traced to a real fact and independently fact-checked.</p>
          </div>
          <div className="pillar card">
            <span className="n">02</span>
            <h3>Interview Defense Pack</h3>
            <p>The probing questions each tailored resume invites — with truthful answers.</p>
          </div>
          <div className="pillar card">
            <span className="n">03</span>
            <h3>Outcome Loop</h3>
            <p>Learns which themes win responses for you, and adjusts what to lead with.</p>
          </div>
        </div>
      </section>

      {candidate ? (
        <div className="card panel stack" style={{ marginTop: 28 }}>
          <span className="eyebrow">Active profile</span>
          <h2 style={{ margin: 0 }}>{candidate.fullName}</h2>
          <p className="lead" style={{ margin: 0 }}>
            {candidate.profile.currentTitle} · {candidate.profile.seniority} ·{" "}
            {candidate.profile.yearsExperience} yrs · <strong>{candidate.factCount} facts</strong> in
            your ledger.
          </p>
          <p className="muted" style={{ fontSize: 13 }}>
            Skills: {candidate.profile.skills.slice(0, 12).join(", ")}
          </p>
          <div>
            <button className="btn ghost small" onClick={onReset}>
              Start over with a new resume
            </button>
          </div>
        </div>
      ) : (
        <div className="card panel stack" style={{ marginTop: 28 }}>
          <span className="eyebrow">Build your fact base</span>
          <div className="row">
            <div>
              <label>Full name</label>
              <input type="text" value={form.fullName} onChange={set("fullName")} placeholder="Jane Engineer" />
            </div>
            <div>
              <label>Email</label>
              <input type="email" value={form.email} onChange={set("email")} placeholder="jane@example.com" />
            </div>
          </div>
          <div className="row">
            <div>
              <label>Phone (optional)</label>
              <input type="text" value={form.phone} onChange={set("phone")} />
            </div>
            <div>
              <label>Location (optional)</label>
              <input type="text" value={form.location} onChange={set("location")} placeholder="High Point, NC" />
            </div>
          </div>
          <div>
            <label>Upload resume text file (.txt/.md) or paste your base resume</label>
            <input type="file" accept=".txt,.md,text/plain" onChange={loadResumeFile} />
            <textarea
              value={form.baseResumeText}
              onChange={set("baseResumeText")}
              placeholder="Paste the full text of your resume. Vouch extracts a fact base — the only claims it will ever make on your behalf."
            />
          </div>
          {error && <div className="ledger flag"><span className="dot" />{error}</div>}
          <div>
            <button className="btn" onClick={submit} disabled={busy}>
              {busy ? (
                <>
                  <span className="spinner" /> Extracting fact base…
                </>
              ) : (
                "Build my fact base"
              )}
            </button>
          </div>
        </div>
      )}
    </>
  );
}
