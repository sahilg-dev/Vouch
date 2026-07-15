# Vouch — the honest job copilot

> Apply with a resume you can defend, and walk in with the hard questions already answered.

Most job tools compete on volume: apply to more roles, faster. That's a losing axis — the
average posting draws ~242 applicants, most are never seen, and the bulk auto-submit tools
get accounts restricted. **Vouch competes on trust and interview-readiness instead.**

### The differentiator — "truth that compounds"

1. **Honesty Ledger** — your resume is parsed into a *fact base* of verifiable claims. Every
   tailored line must trace back to one, and an independent fact-check pass flags anything
   unsupported. The tailoring can reorder, rephrase, and emphasize — it can never invent.
2. **Interview Defense Pack** *(the wedge no volume tool can copy)* — each tailored resume ships
   with the probing questions a senior interviewer will ask about what it emphasized, plus
   truthful talking points drawn from your fact base, plus the gaps to own honestly.
3. **Outcome Loop** — log what happens to each application; Vouch correlates *which emphasis
   themes* win responses **for you specifically** and recommends what to lead with next.

Vouch never auto-submits. It prepares everything for review, you submit, then you log it — which
keeps your accounts safe and feeds the outcome loop with real signal.

---

## Stack

- **Backend:** .NET 10 Minimal API, EF Core + PostgreSQL (Npgsql), raw `HttpClient` against the
  OpenAI Responses API.
- **Frontend:** React + Vite (plain CSS design system, no build-time UI framework).
- **AI:** OpenAI (`gpt-4.1-mini` by default) for parsing, matching, tailoring, validation,
  defense pack, and recommendations.
- **Job sources:** Adzuna (keyed aggregator) + Greenhouse & Lever public company boards (no key).

```
backend/
  Program.cs                     # DI + all endpoints
  Domain/                        # entities + enums (Entity<TKey>, AuditableEntity)
  Data/AppDbContext.cs
  OpenAI/OpenAiClient.cs       # Responses API wrapper + JSON-output helper
  Services/
    ResumeParsingService.cs      # profile + fact base (the ledger's foundation)
    MatchingService.cs           # fit score + strengths/gaps
    TailoringService.cs          # tailor + validate + defense pack
    InsightsService.cs           # outcome loop (stats in C#, narration by OpenAI)
    PrefillService.cs            # review-then-apply assembly
  Ingestion/                     # IJobSource + Adzuna/Greenhouse/Lever + orchestrator
frontend/
  src/views/                     # Setup, Discover, Tracker, Insights
  src/components/                # MatchCard, TailoredModal, ApplyModal
```

---

## Run it (macOS)

You need: Docker, the .NET 10 SDK, and Node 18+. An **OpenAI API key** is required; **Adzuna
keys** are optional (without them, only Greenhouse/Lever boards are searched).

### 1. Start Postgres

```bash
cd jobcopilot
docker compose up -d
```

### 2. Backend

```bash
cd backend
cp .env.example .env          # then edit .env and paste your OPENAI_API_KEY
dotnet restore
dotnet run                    # serves http://localhost:5080 (creates schema on first run)
```

Get free Adzuna keys at https://developer.adzuna.com/ and add them to `.env` for aggregator
coverage. Greenhouse/Lever need no keys — just company slugs (the name in their careers URL,
e.g. `boards.greenhouse.io/`**`stripe`**).

### 3. Frontend

```bash
cd frontend
cp .env.example .env           # optional; defaults to http://localhost:5080
npm install
npm run dev                    # http://localhost:5173
```

### 4. Use it

1. **Profile** — paste your resume; Vouch extracts your profile + fact base.
2. **Discover** — enter a search and some Greenhouse/Lever slugs, hit *Search & score*. Sort by
   most-recent or best-fit. Each card shows the fit score with why-it-fits / watch-outs.
3. **Tailor resume** — opens the Honesty Ledger (resume + per-line fact IDs), the *What changed*
   diff, and the *Defense Pack*.
4. **Review & apply** — review the prefill, open the real posting, submit yourself, *Mark as applied*.
5. **Tracker / Insights** — move applications through the funnel; watch the outcome loop learn.

---

## Honest caveats (read these)

- **Not auto-submit, by design.** v1 prepares and reviews; you click submit. Background
  auto-submit gets LinkedIn/Indeed accounts restricted and produces low-quality applications —
  it's the wrong axis to compete on. The prefill is a field map + tailored text, not a robot that
  drives Workday's shadow-DOM forms.
- **v1 matching uses OpenAI re-ranking, not embeddings.** v1 uses direct LLM scoring. Later, add embeddings/vector search as a prefilter, then OpenAI-rerank the top N.
  Upgrade path below.
- **This code was written without a compiler or network in the authoring environment.** It's
  architecturally complete and internally consistent, but budget time for a couple of small
  fix-ups on first `dotnet run` / `npm run dev` (a missing using, a serialization edge case).
- **LLM JSON can occasionally misbehave.** The OpenAI client strips code fences and throws a
  clear error if parsing fails; services degrade gracefully where it matters (e.g. Insights).
- **Cost.** Tailoring is 3 OpenAI calls per job; matching is ~1 call per 5 jobs. Cap `maxToScore`
  on ingest and tailor on demand (already the default behavior).

## Upgrade path to v2

- **Semantic prefilter:** add pgvector + Voyage AI embeddings; embed JDs on ingest, vector-search
  the candidate profile, and only OpenAI-rerank the top N. (Matching contract already isolates this.)
- **Resume file upload:** parse PDF/DOCX to text before fact-base extraction.
- **Background ingestion:** move ingest+score into a Hangfire job with daily alerts on new high-fit roles.
- **Export:** render the tailored resume to DOCX/PDF (keep the fact IDs in a reviewer-only layer).
- **Defense Pack practice mode:** turn the questions into a timed mock-interview drill.
```
