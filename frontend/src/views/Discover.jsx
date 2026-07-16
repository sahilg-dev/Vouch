import { useEffect, useState } from "react";
import { api } from "../lib/api.js";
import MatchCard from "../components/MatchCard.jsx";
import TailoredModal from "../components/TailoredModal.jsx";
import ApplyModal from "../components/ApplyModal.jsx";

export default function Discover({ candidate, flash }) {
  const [query, setQuery] = useState(candidate?.profile?.currentTitle || "software engineer");
  const [country, setCountry] = useState("us");
  const [gh, setGh] = useState("");
  const [lever, setLever] = useState("");
  const [sort, setSort] = useState("recent");
  const [matches, setMatches] = useState([]);
  const [loading, setLoading] = useState(true);
  const [ingesting, setIngesting] = useState(false);
  const [error, setError] = useState(null);

  const [showPaste, setShowPaste] = useState(false);
  const [pasting, setPasting] = useState(false);
  const emptyJob = { title: "", company: "", location: "", applyUrl: "", description: "" };
  const [pj, setPj] = useState(emptyJob);

  const [tailorTarget, setTailorTarget] = useState(null);
  const [applyTarget, setApplyTarget] = useState(null);
  const [tailoredIds, setTailoredIds] = useState({}); // jobId -> tailoredResumeId

  const [savedSearches, setSavedSearches] = useState([]);
  const [savingSearch, setSavingSearch] = useState(false);

  const load = async (s = sort) => {
    setLoading(true);
    try {
      setMatches(await api.matches(candidate.id, s));
    } catch (e) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  const loadSavedSearches = () => api.savedSearches(candidate.id).then(setSavedSearches).catch(() => {});

  useEffect(() => {
    load();
    loadSavedSearches();
    // Visiting Discover is the natural "seen it" moment for the new-match badge.
    api.markMatchesViewed(candidate.id).catch(() => {});
  }, [candidate.id]);

  const saveSearch = async () => {
    setSavingSearch(true);
    try {
      await api.createSavedSearch(candidate.id, {
        query,
        country,
        greenhouseCompanies: gh.split(",").map((s) => s.trim()).filter(Boolean),
        leverCompanies: lever.split(",").map((s) => s.trim()).filter(Boolean),
      });
      flash("Saved — this search will re-run automatically and surface new matches.");
      await loadSavedSearches();
    } catch (e) {
      setError(e.message);
    } finally {
      setSavingSearch(false);
    }
  };

  const removeSavedSearch = async (id) => {
    await api.deleteSavedSearch(id);
    await loadSavedSearches();
  };

  const ingest = async () => {
    setIngesting(true);
    setError(null);
    try {
      const res = await api.ingest({
        candidateId: candidate.id,
        query,
        country,
        greenhouseCompanies: gh.split(",").map((s) => s.trim()).filter(Boolean),
        leverCompanies: lever.split(",").map((s) => s.trim()).filter(Boolean),
      });
      flash(`Found ${res.newPostings} new roles · scored ${res.scored}.`);
      await load();
    } catch (e) {
      setError(e.message);
    } finally {
      setIngesting(false);
    }
  };

  // Most large employers (Kaiser Permanente, Westat, anyone on Taleo/Workday/iCIMS)
  // are on none of the aggregators, so search can never reach them. Paste the JD.
  const addPastedJob = async () => {
    if (!pj.title.trim() || !pj.company.trim() || !pj.description.trim()) {
      setError("Title, company and description are required.");
      return;
    }
    setPasting(true);
    setError(null);
    try {
      const res = await api.createJob({
        title: pj.title.trim(),
        company: pj.company.trim(),
        location: pj.location.trim() || null,
        applyUrl: pj.applyUrl.trim() || null,
        description: pj.description,
      });

      // Score it. With no company slugs, ingest fetches nothing from the boards but
      // still runs the scoring pass over every posting that has no match yet —
      // which now includes this one, and PostedAt=now puts it first.
      await api.ingest({
        candidateId: candidate.id,
        query: pj.title.trim(),
        country,
        greenhouseCompanies: [],
        leverCompanies: [],
      });

      flash(
        res.deduped
          ? "Already had that job saved — rescored it."
          : `Added "${pj.title.trim()}" at ${pj.company.trim()} and scored the fit.`
      );
      setPj(emptyJob);
      setShowPaste(false);
      await load();
    } catch (e) {
      setError(e.message);
    } finally {
      setPasting(false);
    }
  };

  const changeSort = (s) => {
    setSort(s);
    load(s);
  };

  return (
    <>
      <div className="card panel stack" style={{ marginTop: 8 }}>
        <span className="eyebrow">Find roles that fit your experience</span>
        <div className="row">
          <div>
            <label>What are you looking for?</label>
            <input type="text" value={query} onChange={(e) => setQuery(e.target.value)} />
          </div>
          <div>
            <label>Country (Adzuna)</label>
            <select value={country} onChange={(e) => setCountry(e.target.value)}>
              <option value="us">United States</option>
              <option value="gb">United Kingdom</option>
              <option value="ca">Canada</option>
              <option value="au">Australia</option>
              <option value="in">India</option>
            </select>
          </div>
        </div>
        <div className="row">
          <div>
            <label>Greenhouse company boards (comma-separated slugs)</label>
            <input type="text" value={gh} onChange={(e) => setGh(e.target.value)} placeholder="stripe, airbnb, figma" />
          </div>
          <div>
            <label>Lever company boards (comma-separated slugs)</label>
            <input type="text" value={lever} onChange={(e) => setLever(e.target.value)} placeholder="netflix, brex" />
          </div>
        </div>
        <p className="muted" style={{ fontSize: 12, margin: 0 }}>
          Slugs are the company name in their careers URL, e.g. boards.greenhouse.io/<strong>stripe</strong>.
          Adzuna needs API keys in <code>.env</code>; company boards work without keys.
        </p>
        <div style={{ display: "flex", gap: 10 }}>
          <button className="btn" onClick={ingest} disabled={ingesting}>
            {ingesting ? (
              <>
                <span className="spinner" /> Searching &amp; scoring fit…
              </>
            ) : (
              "Search & score"
            )}
          </button>
          <button className="btn ghost small" onClick={saveSearch} disabled={savingSearch || !query.trim()}>
            {savingSearch ? "Saving…" : "Save this search"}
          </button>
        </div>
        {error && <div className="ledger flag"><span className="dot" />{error}</div>}

        {savedSearches.length > 0 && (
          <div style={{ marginTop: 4 }}>
            <span className="eyebrow">Saved searches (re-run automatically)</span>
            <div style={{ marginTop: 8 }}>
              {savedSearches.map((s) => (
                <div key={s.id} className="tagrow" style={{ gridTemplateColumns: "1fr auto auto" }}>
                  <span>
                    {s.query}
                    {s.greenhouseCompanies.length > 0 && ` · GH: ${s.greenhouseCompanies.join(", ")}`}
                    {s.leverCompanies.length > 0 && ` · Lever: ${s.leverCompanies.join(", ")}`}
                  </span>
                  <span className="muted" style={{ fontSize: 12 }}>
                    {s.lastRunAt ? `last ran ${new Date(s.lastRunAt).toLocaleString()}` : "not run yet"}
                  </span>
                  <button className="btn ghost small" onClick={() => removeSavedSearch(s.id)}>
                    Remove
                  </button>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>

      <div className="card panel stack" style={{ marginTop: 14 }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <div>
            <span className="eyebrow">Not on a job board?</span>
            <p className="muted" style={{ fontSize: 12, margin: "4px 0 0" }}>
              Most large employers run their own ATS and never appear on Adzuna, Greenhouse or Lever.
              Paste the description and Vouch will score, tailor and defend it the same way.
            </p>
          </div>
          <button className="btn ghost small" onClick={() => setShowPaste((v) => !v)}>
            {showPaste ? "Cancel" : "Paste a job description"}
          </button>
        </div>

        {showPaste && (
          <>
            <div className="row">
              <div>
                <label>Job title *</label>
                <input
                  type="text"
                  value={pj.title}
                  onChange={(e) => setPj({ ...pj, title: e.target.value })}
                  placeholder="Chief Engineer, AI Developer Experience &amp; Platform"
                />
              </div>
              <div>
                <label>Company *</label>
                <input
                  type="text"
                  value={pj.company}
                  onChange={(e) => setPj({ ...pj, company: e.target.value })}
                  placeholder="Kaiser Permanente"
                />
              </div>
            </div>
            <div className="row">
              <div>
                <label>Location</label>
                <input
                  type="text"
                  value={pj.location}
                  onChange={(e) => setPj({ ...pj, location: e.target.value })}
                  placeholder="Greensboro, NC"
                />
              </div>
              <div>
                <label>Apply URL</label>
                <input
                  type="text"
                  value={pj.applyUrl}
                  onChange={(e) => setPj({ ...pj, applyUrl: e.target.value })}
                  placeholder="https://..."
                />
              </div>
            </div>
            <div>
              <label>Job description *</label>
              <textarea
                rows={10}
                value={pj.description}
                onChange={(e) => setPj({ ...pj, description: e.target.value })}
                placeholder="Paste the full posting text here — responsibilities, qualifications, everything."
              />
            </div>
            <div>
              <button className="btn" onClick={addPastedJob} disabled={pasting}>
                {pasting ? (
                  <>
                    <span className="spinner" /> Adding &amp; scoring fit…
                  </>
                ) : (
                  "Add & score"
                )}
              </button>
            </div>
          </>
        )}
      </div>

      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", margin: "24px 4px 14px" }}>
        <h2 style={{ margin: 0, fontSize: 18 }}>
          {matches.length} matched {matches.length === 1 ? "role" : "roles"}
        </h2>
        <div className="nav">
          <button className={sort === "recent" ? "active" : ""} onClick={() => changeSort("recent")}>
            Most recent
          </button>
          <button className={sort === "score" ? "active" : ""} onClick={() => changeSort("score")}>
            Best fit
          </button>
        </div>
      </div>

      {loading ? (
        <div className="empty"><span className="spinner" /> Loading matches…</div>
      ) : matches.length === 0 ? (
        <div className="card empty">
          No roles yet. Add a few Greenhouse/Lever company slugs (or Adzuna keys) and run a search.
        </div>
      ) : (
        <div className="stack">
          {matches.map((m) => (
            <MatchCard key={m.matchId} match={m} onTailor={setTailorTarget} onApply={setApplyTarget} />
          ))}
        </div>
      )}

      {tailorTarget && (
        <TailoredModal
          candidate={candidate}
          match={tailorTarget}
          flash={flash}
          onTailored={(id) => setTailoredIds((p) => ({ ...p, [tailorTarget.jobId]: id }))}
          onClose={() => setTailorTarget(null)}
        />
      )}
      {applyTarget && (
        <ApplyModal
          candidate={candidate}
          match={applyTarget}
          tailoredResumeId={tailoredIds[applyTarget.jobId]}
          flash={flash}
          onClose={() => setApplyTarget(null)}
        />
      )}
    </>
  );
}
