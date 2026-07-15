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

  const [tailorTarget, setTailorTarget] = useState(null);
  const [applyTarget, setApplyTarget] = useState(null);
  const [tailoredIds, setTailoredIds] = useState({}); // jobId -> tailoredResumeId

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

  useEffect(() => {
    load();
  }, [candidate.id]);

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
        <div>
          <button className="btn" onClick={ingest} disabled={ingesting}>
            {ingesting ? (
              <>
                <span className="spinner" /> Searching &amp; scoring fit…
              </>
            ) : (
              "Search & score"
            )}
          </button>
        </div>
        {error && <div className="ledger flag"><span className="dot" />{error}</div>}
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
