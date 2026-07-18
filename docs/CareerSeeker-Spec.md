# CareerSeeker — Product & Technical Specification v0.9
### An autonomous career seek-and-apply system: free Windows engine (.exe) + paid Android dashboard (.apk)

**Document status:** Founding spec for review · **Author:** Claude (Anthropic) for Brandon · **Date:** June 11, 2026

---

## Part 1 — Competitive Teardown

Five reference systems were reviewed. The first two share a codebase (AKCodez is a fork of santifer), so the field is effectively four distinct design philosophies. What follows is what each gets right, where each falls short for our purposes, and what CareerSeeker should steal, fix, or reject.

### 1.1 santifer/career-ops (45.4k ★) — "the filter, not the cannon"

This is the market leader and the most mature thinking in the space. Its core insight is contrarian and correct: the job search problem is not "apply to more jobs," it's "stop wasting time on the wrong jobs." It evaluates 740+ listings to find the few worth pursuing, scoring each on ten weighted dimensions with an A–F grade, and explicitly recommends against applying to anything below 4.0/5.

**Best aspects worth stealing.** The 6-block evaluation (role summary, CV match, level strategy, comp research, personalization, interview prep) treats a job posting as an intelligence target, not a form to fill. The Interview Story Bank — accumulating STAR+Reflection stories *across* evaluations until the user has 5–10 master stories — is the single smartest feature in any of these repos: it converts the byproduct of applying into durable career capital. The portal scanner targets Greenhouse/Ashby/Lever directly (structured ATS platforms with stable JSON endpoints) rather than fighting LinkedIn's anti-bot defenses. Batch processing with parallel sub-agent workers is the right concurrency model. The pipeline-integrity tooling (dedup, status normalization, liveness checks on stale postings) shows hard-won operational scar tissue.

**Worst aspects for our purposes.** It is a developer tool wearing a product costume: it requires Node, Playwright, a Claude Code subscription, YAML editing, and comfort with a terminal. Its data layer is markdown tables and TSV files — charming for one hacker, catastrophic for a consumer product (no transactions, no concurrent access, no sync). It deliberately never submits anything, which is a defensible philosophy but leaves the most painful 40% of the job (form-filling, follow-ups, scheduling) on the human. And the Go TUI dashboard requires compiling Go. Our target user has never compiled anything.

### 1.2 AKCodez/career-ops (fork) — the auto-apply experiment

The fork's additions are exactly the pieces santifer refused to build, which makes it the most useful single reference for CareerSeeker's autonomous tier.

**Best aspects.** `fetch-jd.mjs` is quietly brilliant: it hits the Ashby/Greenhouse/Lever *public JSON APIs* directly instead of scraping rendered pages — zero LLM tokens, zero ToS friction, zero "the page only returned a title" failures. This is the correct ingestion architecture and CareerSeeker adopts it wholesale. `/career-ops submit` does browser-automated form-fill (every field, dropdowns, long-form answers, cover letter paste) but **pauses before the Submit button** — a graduated-autonomy pattern, not a binary one. The one-sentence onboarding ("onboard me — I'm [role], based in [city], CV pasted below") that auto-populates all config files is the right UX instinct.

**Worst aspects.** It inherits all of santifer's consumer-hostility, and the pause-before-submit step still requires the user to be sitting at the machine — which defeats background autonomy. The fix isn't removing the pause; it's *relocating* it to a phone notification (see §2.2).

### 1.3 muggl3mind/career-manager — the onboarding blueprint

A small personal pipeline, but it has the best **onboarding interview** design of the five and the cleanest separation of concerns.

**Best aspects.** Onboarding reads the user's existing resume *first* and pre-fills the profile so the interview asks fewer questions — interview by exception, not interrogation. Each skill owns its data with explicit interfaces and a documented ownership matrix (`target-companies.csv` belongs to job-search; `applications.csv` belongs to job-tracker) — that's real software architecture. The company-research "dossier" concept (overview, signals, fit, risks per company) is a feature users would screenshot. The smoke test (`smoke_test.py` validating deps/config/imports before the pipeline runs) and graceful degradation (JobSpy scraping is optional; web search covers when it breaks) are production instincts. Optional Gmail digest integration foreshadows our Drafts mode.

**Worst aspects.** It leans on JobSpy scraping of LinkedIn/Indeed, which the README itself admits "may not always work reliably" — scraping hostile boards is a treadmill, not a foundation. CSV files as the source of truth. No packaging, no scheduler, no autonomy: the user must invoke every run.

### 1.4 wexxwuther/job-hunter — the safety and trust engineering

The least-starred repo with the most important ideas. Whoever built this thought hardest about what goes wrong when an AI touches your career.

**Best aspects — and these are non-negotiable adoptions for CareerSeeker:**

The **ghost-job legitimacy score** is a separate axis from fit. Five sub-scores (cv_match 0.35, comp_vs_target 0.25, cultural_signals 0.20, posting_legitimacy 0.20) plus a **red-flags multiplier** where one severe flag (pays-in-equity-only, asks for SSN pre-offer, fee-to-apply) torpedoes the whole score from 5.0 to 1.0. Wasting an autonomous agent's cycles on ghost jobs is bad; autonomously sending a user's PII to a scam posting is product-ending. This dual-axis model is the heart of our scorer.

The **anti-fabrication verification gate** exists because they shipped the bug: earlier versions of their resume-tailor *invented accomplishments*, because "push for numbers and outcomes" was an active instruction while "don't fabricate" was a passive principle, and LLMs follow active instructions. Their fix — a mandatory `verify_no_fabrication.py` pass that diffs every claim in the tailored output against the source resume and blocks the write on any unsupported claim — is the most important 100 lines of code in this entire space. **An autonomous applier without this gate is a defamation machine pointed at its own user.** A hiring manager who catches one fabricated certification doesn't just reject the application; in regulated industries it can be disqualifying or unlawful.

The **local learning loop** is the right memory model: plain-markdown DECISIONS / LESSONS / OUTCOMES / REJECTED_IDEAS files, a cold-start guard (no pattern-mining until ≥5 closed-loop outcomes), and **opt-in lessons** — the agent proposes "4 of 5 rejections cite comp — want me to remember this?" and writes nothing without confirmation. Bounded influence (lessons tune grading, never the global weights). Deterministic, inspectable, deletable.

Also: explicitly designed for nurses, welders, teachers — not just tech (50-state workforce boards, niche boards per industry). That's the actual consumer market. And the orchestrator-routes-to-five-member-skills decomposition maps cleanly onto a service with worker modules.

**Worst aspects.** Hard no on auto-submission and even on auto-sending follow-up emails ("a load-bearing safety test enforces it") — principled, but it caps the product at "very good document generator." Our job is to earn the trust model they declined to attempt, not to ignore why they declined. HTML-file tracker, no background operation, no mobile surface.

### 1.5 Rahat-Kabir/job-search-agent — the service architecture

The only one built as an actual multi-user service: FastAPI + Postgres + Redis + React, LangGraph/DeepAgents orchestration.

**Best aspects.** **Two-phase search** — cheap broad pass (Tavily/Brave → 15 scored candidates) then expensive deep scrape (Firecrawl) only on user-selected jobs — is the correct cost-control shape for any LLM pipeline; CareerSeeker generalizes it into a four-stage funnel where each stage spends ~10× more compute on ~10× fewer items. **Human-in-the-loop interrupts before external API calls** built into the agent graph (not bolted on) is the right place for approval gates to live. Real SSE streaming of agent events (not polling) is exactly what the Android dashboard needs. Proper relational schema (User → Profile/Preferences/Sessions → Results) instead of CSVs.

**Worst aspects.** It stops at *search* — no tailoring, no applying, no tracking, no follow-up; a demo of the first 20% of the funnel. Web search engines are a low-precision job-discovery source compared to ATS APIs (lots of aggregator spam). ~30–50% token overhead from sub-agent context passing — a warning to budget context hygiene from day one. No auth ("user identified by X-User-ID header") — fine for a demo, instructive as a what-not-to-do.

### 1.6 Synthesis: the gap in the market

Plot all five on two axes — *autonomy* and *consumer-readiness* — and the upper-right quadrant is empty:

```
                    consumer-ready
                          ▲
                          │
   (hosted SaaS appliers: │        ★ CareerSeeker
    LazyApply etc. — high │          (the empty quadrant:
    autonomy, low trust,  │           autonomous + trustworthy
    spray-and-pray)       │           + zero-terminal install)
                          │
  ◄───────────────────────┼───────────────────────►
   low autonomy           │            high autonomy
                          │
   Rahat-Kabir ●          │   ● AKCodez (pauses at Submit,
   (search only)          │     user must be at keyboard)
   muggl3mind ●           │
   job-hunter ● ● santifer│
   (all: human submits    │
    everything, terminal  │
    required)             ▼
                    hacker-only
```

Every credible open-source system refuses to submit; every commercial auto-applier earns its bad reputation by spraying fabricated, untracked applications. The winning product is **supervised autonomy**: an engine that genuinely runs itself in the background, with the safety engineering of job-hunter, the ingestion architecture of AKCodez, the onboarding of muggl3mind, the evaluation depth of santifer, the service skeleton of Rahat-Kabir — and a phone in the user's pocket as the approval surface that lets autonomy be both real *and* accountable.

---

## Part 2 — Strategic Design Decisions (where this spec amends the brief)

Three parts of the original brief, taken literally, would hurt the product. Each gets a diagnosis and a concrete replacement that preserves the intent.

### 2.1 "Set and forget forever" → "Set, and forget until it matters"

The brief: after onboarding, "the user should never have to interact with it again."

Notice that all five reference systems — including the fork whose whole purpose was auto-apply — independently refused full zero-touch submission. That convergence isn't timidity; it's five teams discovering the same failure modes:

**Fabrication compounds silently.** job-hunter shipped a tailor that invented credentials despite good intentions, and only caught it because users reviewed output. Remove the user and nothing catches it. An engine submitting 30 applications a week with a 2% fabrication rate puts a false claim in front of an employer roughly every two weeks, in the user's name, on documents that get cross-checked at background-check time.

**Impersonation in correspondence.** Auto-replying to a recruiter's email *as the user* is materially different from drafting a resume. Recruiters ask things with consequences: "Are you authorized to work in the US?", "What's your current comp?", "Can you start Monday?". A wrong autonomous answer can be a lie told in the user's name, a negotiation position destroyed, or a job lost. And interview scheduling commits the *human's* actual hours — an agent should never book time the human hasn't blessed.

**Strategy drift.** A job search is a campaign, not a batch job. After two weeks of silence the right move might be to change targeting, not to keep firing. Zero-touch removes the only feedback channel.

**The replacement — three autonomy levels chosen at onboarding, switchable anytime:**

| Level | Name | What the engine does alone | What requires the human |
|---|---|---|---|
| **L1** | **Drafts** | Discovers, scores, researches, tailors, packages each application as a ready-to-send Gmail draft | One click per send (review in Gmail) |
| **L2** | **Autopilot + Gates** *(flagship)* | Everything in L1, **plus** auto-submits applications, sends template-class follow-ups, proposes interview slots | One **tap** per gated event, from the phone or a daily digest email |
| **L3** | **Full Autopilot** | Everything, within hard rails: pre-approved answer bank only, novel questions auto-escalate, calendar booking only inside pre-authorized windows, hard caps (≤N apps/day, score ≥ threshold), immutable audit log, kill switch | Reads a weekly summary; answers escalations at leisure |

L2 is the magic trick: the engine runs 24/7 on the PC, and "interaction" collapses to tapping ✓ on a push notification — *"Apply to Staff Engineer @ Stripe (fit 4.6, legit 4.8)? [Approve] [Skip] [View]"*. Ten seconds a day, full autonomy in effect, zero unreviewed actions in the user's name. L3 exists for users who explicitly sign the "autonomy contract" during onboarding (§4.4) and is rails-bound by construction, not by promise.

Two rules are invariant across all levels, enforced in code with load-bearing tests (job-hunter's pattern):

1. **The Fabrication Gate (§6.4) can never be bypassed.** No claim leaves the machine unless it traces to the user's source-of-truth profile. At any level.
2. **Novel free-text in the user's voice is never auto-sent.** Template-class messages with slot-filling (follow-up nudges, thank-yous, slot confirmations) can be pre-approved as a class; anything the classifier marks novel (comp questions, "why did you leave," visa status) escalates. At any level.

### 2.2 The paywall: sell convenience, never sell visibility

The brief: the engine acts autonomously for free, but seeing *what it's doing* costs $1–5.

Run that through the L2/L3 lens and the problem is sharp: the free tier would be **an agent applying to jobs, emailing employers, and booking calendar slots in your name, while the window into its activity sits behind a paywall.** That's not a freemium split, it's selling the smoke detector separately from the stove. It's also a one-star-review generator ("it applied to my own employer and I couldn't see it") and a regulatory soft target.

The amended split keeps your exact price point and makes the paid app *more* compelling, because it gates the thing that makes autonomy pleasant rather than the thing that makes it safe:

**Free (.exe alone):** full engine at every autonomy level, *with* full local visibility — a local web dashboard at `http://localhost:7777` (the engine already runs an HTTP server for IPC; rendering a status page is nearly free), Gmail drafts/labels as a natural audit trail, and a daily digest email where L2 gates can be approved via magic links. Nobody is ever blind.

**Paid (.apk, $4.99 one-time):** the *remote, real-time, push-driven* experience — live dashboard anywhere, push-notification approval gates (the 10-second L2 loop), remote pause/kill switch, interview-day briefing cards, weekly analytics. The pitch writes itself: *"Your job search runs at home. Approve applications from the bus."* The localhost dashboard converts users to the apk instead of substituting for it, because the moment you leave the house, push approvals are the product.

**The honest economics problem you must solve before launch:** a "free" engine that does deep LLM work (scoring hundreds of postings, tailoring documents, drafting emails) has a real marginal cost — order of $5–20/month per active user in inference. A $4.99 one-time apk cannot subsidize that. The spec therefore defines a pluggable **LLM Gateway** (§5.6) with three modes: (a) **BYOK** — user supplies their own Anthropic/OpenAI API key, truly free for us, the hacker tier; (b) **Managed** — our proxy with metered quota, which is where a future $9–15/mo "CareerSeeker Pro" subscription lives (unlimited inference + apk included); (c) **Local** — small local models (Phi/Llama-class via llama.cpp) for the cheap pipeline stages (classification, dedup, extraction), reserving cloud calls for tailoring and correspondence. Mode (c) is also a genuine differentiator: "your resume never leaves your machine for 80% of the pipeline."

### 2.3 "Just two binaries" needs one thin third leg

Two binaries cannot deliver "real-time dashboard": the .exe sits behind home NAT; the phone is on LTE. They can't connect directly, reliably. Three honest options:

1. **Relay service (recommended):** a deliberately dumb cloud component — WebSocket relay + push-notification sender + encrypted-blob store for offline catch-up. It stores **only end-to-end-encrypted event blobs** (keys exchanged via QR pairing, §8.3); the relay cannot read resumes, emails, or job data. Tiny, cheap (single Cloudflare Worker / Fly.io app class), and it's what makes push gates possible at all.
2. Same-Wi-Fi only (mDNS discovery): no server, but the dashboard dies the moment the user leaves home — which is precisely when they want it. Ship as offline fallback, not the architecture.
3. Drive/Dropbox file-sync as a poor-man's bus: latency in minutes, no push. Rejected.

The spec assumes option 1 with option 2 as automatic fallback. "Local-first, relay-blind" is both the architecture and the privacy marketing line.

### 2.4 Where the engine is allowed to apply (the ToS map)

Channel strategy matters more than scraping cleverness. CareerSeeker's automated submission targets, in priority order: **(1) Direct-ATS postings** — Greenhouse / Lever / Ashby / Workable host the application form on predictable URLs with stable structure; AKCodez-style JSON ingestion + Playwright form-fill is reliable and low-friction here, and these platforms host a huge share of startup/mid-market jobs. **(2) Email-channel applications** — postings that say "send resume to jobs@…" are a perfect fit for the Gmail-native pipeline and involve no third-party automation at all. **(3) Company career pages** with simple forms. **LinkedIn "Easy Apply" and Indeed are read-only sources at most**: both prohibit automated application in their ToS, aggressively fingerprint automation, and ban accounts — an autonomous consumer product that gets its users' LinkedIn accounts banned is dead on arrival. For postings that exist only behind such walls, the engine does everything *except* submit and queues a "finish this one yourself (2 min)" task with a deep link. Honest about the boundary, still 10× less work than manual.


---

## Part 3 — System Overview

### 3.1 Component map

```
┌──────────────────────────── USER'S WINDOWS PC ─────────────────────────────┐
│                                                                             │
│  CareerSeeker.exe  (single signed installer; .NET 8 self-contained)         │
│  ┌───────────────────────────────────────────────────────────────────────┐ │
│  │ SeekerSvc — Windows Service ("CareerSeeker Engine")                   │ │
│  │                                                                       │ │
│  │   Orchestrator (state machine + job queue, Quartz.NET scheduler)      │ │
│  │     ├─ Scout        ATS feeds, email-channel postings, RSS, plugins   │ │
│  │     ├─ Scorer       fit × legitimacy, red-flag multiplier             │ │
│  │     ├─ Researcher   company dossier builder (web search, cached)      │ │
│  │     ├─ Tailor       resume/cover-letter generation (HTML→PDF)         │ │
│  │     ├─ Verifier     FABRICATION GATE — claim-by-claim provenance      │ │
│  │     ├─ Dispatcher   Gmail drafts / Gmail send / Playwright form-fill  │ │
│  │     ├─ Correspondent inbound-email classifier + reply pipeline        │ │
│  │     ├─ Schedulist   interview slot negotiation + Calendar writes      │ │
│  │     └─ Librarian    learning loop, story bank, weekly strategy review │ │
│  │                                                                       │ │
│  │   LLM Gateway      BYOK ▸ Managed ▸ Local — per-stage routing         │ │
│  │   Connector layer  Gmail API · Calendar API · ATS JSON · Playwright   │ │
│  │   Store            SQLite (WAL) + DPAPI-encrypted secrets vault       │ │
│  │   Local API        http://127.0.0.1:7777 (REST + SSE) + localhost UI  │ │
│  │   Sync client      E2E-encrypted event log ⇄ relay                    │ │
│  └───────────────────────────────────────────────────────────────────────┘ │
│  Tray app (WinUI 3): status glyph, pause, open dashboard, onboarding host  │
└───────────────────────────────────┬─────────────────────────────────────────┘
                                    │  WSS — E2E-encrypted envelopes
                          ┌─────────▼──────────┐
                          │  Relay (blind)     │   stores ciphertext only;
                          │  ws fan-out · FCM  │   cannot read user data
                          │  push · blob queue │
                          └─────────┬──────────┘
                                    │  WSS + FCM push
┌───────────────────────────────────▼─────────────────────────────────────────┐
│  CareerSeeker Dashboard.apk ($4.99 · Kotlin + Jetpack Compose, minSdk 26)   │
│   Pipeline board · Approval gates (push, actionable notifications)          │
│   Job detail w/ dossier · Interview-day cards · Kill switch · Analytics     │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.2 The funnel (two-phase search, generalized)

Each stage spends roughly an order of magnitude more compute on an order of magnitude fewer items — Rahat-Kabir's two-phase idea stretched across the whole pipeline:

```
 ~1,000/wk   DISCOVERED   cheap: ATS JSON pulls, dedup hash, hard-filter (local model)
   ~150/wk   SCREENED     mid:   fit×legitimacy quick score on title+snippet
    ~40/wk   EVALUATED    deep:  full JD fetch, 6-block evaluation, dossier delta
    ~15/wk   TAILORED     heavy: bespoke resume + cover letter + answers → GATE
  ~5–15/wk   DISPATCHED   per autonomy level: draft / gated submit / rails submit
  ongoing    NURTURED     follow-ups, replies, scheduling, outcome capture
```

### 3.3 Application lifecycle state machine

```
DISCOVERED → SCREENED → EVALUATED ─┬→ REJECTED_BY_ENGINE (score < threshold)
                                   └→ TAILORED → VERIFIED ─┬→ BLOCKED_FABRICATION (rework or escalate)
                                                            └→ READY
READY ─┬→ DRAFTED (L1: sits in Gmail Drafts)
       ├→ GATE_PENDING (L2: push sent) ─┬→ APPROVED → SUBMITTING → APPLIED
       │                                ├→ SKIPPED (user declined; reason captured → Librarian)
       │                                └→ GATE_EXPIRED (72h; falls back to digest email)
       └→ SUBMITTING (L3, within rails) → APPLIED
APPLIED → AWAITING_RESPONSE ─┬→ RECRUITER_REPLY → CORRESPONDENCE (template-class auto / novel escalate)
                             ├→ FOLLOWUP_DUE (day 8, day 16) → FOLLOWUP_SENT
                             ├→ INTERVIEW_PROPOSED → SLOTS_OFFERED → SCHEDULED → calendar event
                             ├→ REJECTED → outcome harvested → Librarian
                             ├→ OFFER → 🎉 escalate immediately, engine pauses comp talk to human
                             └→ GHOSTED (30d) → archived, counts against company in scorer
Any state → USER_KILLED (kill switch) | PAUSED
```

Every transition appends an immutable row to `events` (the audit log) and, when sync is on, an encrypted envelope to the relay.

---

## Part 4 — Onboarding (the .exe first-run experience)

Onboarding is the product's first impression and the engine's entire worldview; it deserves spec-level care. It runs in the tray app as a wizard (15–25 min, resumable), following muggl3mind's principle: **read documents first, interview by exception.**

### 4.1 Phase A — Identity & ingestion (3 min)
Welcome → choose data locale → drag-and-drop resume(s), LinkedIn profile export, old cover letters, portfolio URLs. The engine parses everything (DOCX/PDF/text) into a draft **Source-of-Truth Profile** before asking a single question. A progress panel shows what it learned ("Found 9 years experience, 3 employers, 14 skills, 2 gaps I'll ask about").

### 4.2 Phase B — North-Star interview (8–12 min, adaptive)
A conversational form (chat-style UI, but every answer maps to a typed field — no free-text soup). It asks **only what the documents didn't answer**, drawn from four banks:

1. **Targeting** — roles/titles (with synonym expansion preview: "SDE = SWE = Software Engineer — edit?"), industries in/out, seniority band, location + remote stance, company size/stage taste, mission constraints ("no defense, no gambling" → hard REJECTED_IDEAS list, never re-asked).
2. **Economics** — comp floor / target / stretch (normalized across yr/mo/hr), equity appetite, benefits dealbreakers, notice period, earliest start.
3. **Truth & gaps** — the anti-fabrication interview: "Your resume shows a gap 2023–2024 — one sentence I'm allowed to use?"; "You list 'Kubernetes' — production experience or coursework?" Every claim gets a confidence tag (`verified | stated | weak`) that the Tailor must respect (weak claims may appear but never be amplified).
4. **Voice** — 3 short writing samples or a 5-question style elicitation so cover letters and emails sound like the user, not like a model. Stored as a style card (tone markers, sentence length, banned phrases like "I am thrilled").

### 4.3 Phase C — Accounts & channels (4 min)
Google OAuth (incremental scopes: `gmail.compose` always; `gmail.send` + `gmail.modify` only if L2/L3; `calendar.events` only if scheduling enabled). The wizard creates the Gmail label tree (`CareerSeeker/Outbox`, `/Sent`, `/Replies`, `/Action-Needed`) and a dedicated calendar ("CareerSeeker Interviews"). Optional: pair the Android app now via QR (this performs the E2E key exchange, §8.3).

### 4.4 Phase D — The Autonomy Contract (3 min)
Not a EULA-wall — an explicit, plain-language settings ritual the user will remember agreeing to:

- Choose L1 / L2 / L3 with a one-paragraph honest description of each.
- Set the rails (defaults shown): max applications/day (5), minimum combined score to act (4.0), quiet hours, **employer blocklist seeded automatically with the user's current employer and its domains** (the single most common auto-applier horror story, closed at onboarding).
- Build the **Approved Answer Bank**: the wizard walks the 20 most common application/recruiter questions (work authorization, relocation, salary expectation phrasing, start date, "why are you leaving") and the user approves exact wordings. L2/L3 may only ever auto-answer from this bank; everything else escalates.
- Calendar consent: pre-authorized interview windows (e.g., Tue–Thu 10:00–16:00), buffer rules, max interviews/week.
- Signature line, displayed verbatim: *"CareerSeeker will act on your behalf within these limits. Every action is logged and reviewable. You can pause or stop it at any time from the tray, the dashboard, or by replying STOP to any digest email."* → typed "I agree".

### 4.5 Phase E — Dry run (2 min)
The engine immediately runs Scout+Scorer on live data and presents its first 5 scored matches *before* doing anything autonomous: "Here's how I think. Tune me." Thumbs up/down on these calibrates initial weights and gives the user a felt sense of the scoring brain. Then: "Engine starting. First digest tomorrow 8:00 AM."

---

## Part 5 — The Windows Engine (.exe) in detail

### 5.1 Packaging & runtime

2026 packaging note: prefer Azure Artifact Signing when eligible for public Windows distribution. EV
certificates are no longer a SmartScreen shortcut; the launch blocker is public Authenticode signing, not
EV specifically.
- **Stack:** .NET 8, self-contained single-file publish (no runtime install), ~80 MB. Two processes: `SeekerSvc.exe` (Windows Service, auto-start, recovery=restart) and `SeekerTray.exe` (per-user WinUI 3 tray + wizard host). Installer: Inno Setup or MSIX; **Authenticode-signed (Azure Artifact Signing/OV/EV)** — an unsigned background agent that reads your email is SmartScreen-flagged malware as far as Windows is concerned, so code signing is a launch blocker, not a polish item.
- Why a Service and not a tray-only app: survives logoff, restarts after crash/reboot, runs scheduled work at 3 AM. The tray is a thin client of the local API.
- **Playwright for .NET** with bundled Chromium for form-fill; headed-but-minimized mode with human-pace input (per-field delays, no parallel sessions) — politeness as policy, one application at a time per site.
- Auto-update via Squirrel/Velopack channel, delta updates, staged rollout flag.

### 5.2 Storage
SQLite in WAL mode at `%ProgramData%\CareerSeeker\seeker.db` — the single source of truth (no markdown/CSV/TSV anywhere). Secrets (OAuth refresh tokens, API keys, E2E sync keys) in a separate vault file encrypted with **Windows DPAPI** scoped to the service account; nothing sensitive in the DB proper. Generated artifacts (PDFs, dossiers) on disk under content-addressed paths, rows hold hashes. Nightly encrypted local backup, 14-day rotation; export-workspace / import-workspace commands (job-hunter's portability idea) for machine migration.

### 5.3 Scout — discovery without ToS landmines
Sources, in priority order:
1. **ATS JSON APIs** (Greenhouse `boards-api`, Lever `postings`, Ashby `posting-api`, Workable, SmartRecruiters): a curated, updatable board registry (ships with ~2,000 companies; updates via signed manifest) + per-user additions ("watch these 30 companies"). Cheap, structured, legal.
2. **Email-channel postings** from niche-board newsletters/RSS the user subscribes to (job-hunter's per-industry board list, incl. healthcare/trades/legal/education/government — this is how we serve nurses and welders, not just engineers).
3. **Aggregator read-only**: search-engine and JobSpy-style pulls from LinkedIn/Indeed for *discovery only*, marked `channel=manual_finish`.
4. **Plugin SDK** (signed DLLs / declarative YAML scrapers) so the community can add boards without us shipping updates.

Dedup: normalized `(company, title_canonical, location)` simhash across sources; repost detection (same JD re-listed >2× in 90 days) feeds the legitimacy score. Liveness checks re-verify EVALUATED+ postings every 72h and auto-archive dead links (santifer's scar tissue, adopted).

### 5.4 Scorer — two axes, one multiplier
```
fit        = 0.35·cv_match + 0.25·comp_vs_target + 0.20·growth_signal + 0.20·prefs_alignment
legitimacy = ghost-job rubric (posting age/repost count, recruiter identifiability,
             spec specificity, comp transparency, domain/company verification)
red_flags  = multiplier ∈ {1.0, 0.5, 0.1}: fee-to-apply, SSN/DOB pre-offer,
             pay-in-equity-only, crypto-payroll, reshipping-pattern language → 0.1 hard floor
score      = min(fit, legitimacy_capped) · red_flags        // a scam can never outrank its worst axis
```
Weights are global constants; the Librarian's confirmed lessons adjust *grading inputs* per user (bounded influence, job-hunter's rule). `legitimacy < 2.5` is an absolute dispatch block at every autonomy level — the engine may show the job, never act on it.

### 5.5 Tailor + Verifier — generation behind the gate
Tailor produces resume (HTML template → headless-Chromium PDF, ATS-clean single column, standard fonts), cover letter (≤250 words, style card applied, one researched company-specific hook from the dossier), and answers to application questions (Approved Answer Bank first; novel questions drafted-and-flagged).

**The Fabrication Gate (Verifier)** then runs before anything becomes READY:
1. Decompose tailored output into atomic claims (employer, title, dates, metrics, skills, credentials).
2. Each claim must match a Source-of-Truth Profile entry (exact, paraphrase-entailed via NLI check, or arithmetic-derivable e.g. tenure math). 
3. Unsupported claim → `BLOCKED_FABRICATION`: one rework attempt with the violation named; still failing → human escalation with a diff view ("I wanted to say X; your profile only supports Y").
4. `confidence=weak` claims may be restated, never quantified or upgraded.
5. The gate is a pure function with golden tests; **no autonomy level, config flag, or prompt can route around it.**

### 5.6 LLM Gateway
Per-stage routing table (user-visible, editable):
```
classify/dedup/extract  → local (Phi-3.5/Llama-class, llama.cpp, CPU-ok)   ~free
quick score             → cheap cloud (Haiku-class) or local                ~$0.001/job
full evaluation/dossier → mid cloud (Sonnet-class)                          ~$0.02/job
tailoring/correspondence→ best available (Sonnet/Opus-class)                ~$0.05–0.15/app
```
Modes: **BYOK** (user key, stored in DPAPI vault, calls go direct to provider) · **Managed** (our metered proxy → future Pro subscription) · **Local-max** (everything possible on-device; tailoring quality degrades gracefully and the UI says so). Hard monthly budget setting with engine auto-throttle; per-stage token accounting surfaces in analytics ("this week cost $1.84 / 312 LLM calls").

### 5.7 Correspondent — the inbound brain (L2/L3)
Watches the Gmail label via push (watch/webhook→relay) or 5-min poll. Pipeline: thread → classifier →
`{rejection, auto-ack, interview_request, info_request_template, info_request_novel, offer, scam/phish, other}`.
- rejection → outcome harvested, optional gracious templated reply (user-toggle), state → REJECTED
- interview_request → Schedulist
- info_request_template (e.g. "are you authorized to work in the US?") → answered **verbatim from the Approved Answer Bank**, logged
- info_request_novel / offer / other → `Action-Needed` label + push card with a *drafted* reply the user can edit-and-approve from the phone; **never auto-sent**
- scam/phish (credential asks, payment asks, look-alike domains) → quarantined, user warned, company flagged in scorer

All outbound mail is sent from the user's own Gmail via API (real identity, real sent-mail record — no spoofing, no third-party SMTP), threaded correctly, and rate-limited to human-plausible volume.

### 5.8 Schedulist
On interview_request: parse proposed times or ask for them (template), intersect with free/busy **inside the pre-authorized windows only**, propose 3 slots, and on confirmation write the event (title, hiring contact, conferencing link, prep-dossier attached) to the dedicated calendar + push an interview-day card to the phone (T-24h and T-1h: company brief, interviewers if discoverable, the user's 3 most relevant STAR stories from the Story Bank). At L2 the calendar write itself is a one-tap gate; at L3 it's automatic within windows.

### 5.9 Librarian — memory, learning, and the weekly review
Implements job-hunter's loop natively: outcomes harvested from state transitions; ≥5 closed loops before pattern proposals; every lesson is an opt-in push card ("3 of your 4 interviews came from <200-person companies — weight smaller companies higher? [Yes][No]"); confirmed lessons stored as inspectable rows, deletable individually or wholesale ("forget everything you've learned"). Maintains the **Story Bank** (STAR+R stories accreted across evaluations — santifer's best feature) and composes the **Sunday Strategy Review** email/card: funnel stats, response-rate by channel/title/comp-band, what it plans to change, one question for the user. This is also the anti-drift mechanism from §2.1: the campaign gets steering even when the user never opens a dashboard.

---

## Part 6 — The Android Dashboard (.apk)

### 6.1 Product framing
Price **$4.99 one-time** (top of the stated range; a $0.99 utility reads as junk, $4.99 reads as a tool) on Play Store; the engine is the free razor, the app is the handle you actually hold. Future "Pro" subscription (managed inference + app bundled) layers on top without changing this launch shape.

### 6.2 Stack
Kotlin · Jetpack Compose · minSdk 26 · Room (local replica of the event-sourced state) · OkHttp WSS client to relay · Firebase Cloud Messaging for push (payloads are opaque ciphertext + a key id; plaintext is decrypted on-device — the relay and FCM never see content) · Actionable notifications (Approve/Skip buttons inline) · biometric lock optional.

### 6.3 Screens
1. **Pipeline** — kanban-ish board mirroring the state machine; counts, freshness, search.
2. **Gates** — the queue of pending approvals (applications, novel replies, calendar writes). One tap to approve, long-press for the full artifact (rendered resume PDF, drafted email with diff-vs-template highlighting).
3. **Job detail** — score breakdown (both axes + red flags), company dossier, the exact documents sent, full correspondence thread, timeline.
4. **Interviews** — upcoming cards with prep briefs; post-interview "how did it go?" capture (feeds Librarian).
5. **Engine** — live status, last heartbeat, today's actions, token/cost meter, autonomy level switch, rails editor, **kill switch** (big, red, instant: relay→engine `PAUSE_ALL`, acknowledged within seconds or the app warns the engine is unreachable).
6. **Insights** — weekly funnel, response rates, lesson history, Story Bank browser.

### 6.4 Offline & failure behavior
Event-sourced replica means the app is fully readable offline; approvals queue locally and sync on reconnect with idempotency keys. If the PC is offline, gates show "engine asleep — will act when your PC wakes," and gate timeouts (72h) fall back to the email digest so a dead phone never silently stalls the search.

---

## Part 7 — Data Schemas (the contract everything shares)

### 7.1 SQLite core tables (engine)
```sql
profile(id, json, version, updated_at)                  -- Source-of-Truth Profile (one row)
claims(id, profile_id, kind, text, confidence,          -- atomic verified facts; the Gate's oracle
       source_doc, created_at)
companies(id, name, domain, ats_kind, ats_handle,
          dossier_path, dossier_at, flags)
jobs(id, company_id, source, url, title, title_canon,
     location, remote, comp_min, comp_max, jd_path,
     simhash, first_seen, last_verified, repost_count)
scores(job_id, fit, legitimacy, red_flag_mult, total,
       subscores_json, scored_at, model_used)
applications(id, job_id, state, autonomy_level,
     resume_path, cover_path, answers_json,
     gate_id, submitted_at, channel,                    -- channel: ats_form|email|manual_finish
     created_at, updated_at)
gates(id, application_id NULL, kind,                    -- kind: apply|reply|calendar|lesson
      payload_json, status, requested_at, resolved_at, resolved_via)  -- push|digest|localhost
threads(id, application_id, gmail_thread_id, last_class, last_msg_at)
events(seq INTEGER PRIMARY KEY, ts, actor, kind,        -- append-only audit log; actor: engine|user|relay
       entity, entity_id, payload_json, prev_hash, hash)-- hash-chained: tamper-evident
lessons(id, text, evidence_json, status,                -- proposed|confirmed|rejected
        affects, created_at, resolved_at)
stories(id, title, situation, task, action, result,
        reflection, tags_json, source_app_ids)
config(key, value)                                      -- rails, autonomy level, budgets, quiet hours
```

### 7.2 Sync envelope (engine ⇄ relay ⇄ apk)
```json
{ "v": 1,
  "device": "engine|phone",
  "seq": 48211,
  "ts": "2026-06-11T14:02:11Z",
  "key_id": "k-2026-06-01",
  "nonce": "…",
  "ciphertext": "…"            // XChaCha20-Poly1305( event JSON ), keys from QR pairing
}
```
The relay sees sender, sequence, size, timing — never content. Event kinds inside the ciphertext: `state_change, gate_request, gate_resolve, heartbeat, metric, lesson_proposal, kill, config_change`. Gate resolutions are signed by the phone's device key so the engine can prove "a paired device approved this" in the audit log.

### 7.3 Local API (engine, 127.0.0.1:7777)
```
GET  /v1/status            engine heartbeat, queue depths, cost meter
GET  /v1/pipeline          board view
GET  /v1/jobs/{id}         full record + score breakdown + artifacts
GET  /v1/gates?status=open
POST /v1/gates/{id}        {decision: approve|skip, note}
POST /v1/control           {action: pause|resume|kill, scope}
GET  /v1/events?since=seq  SSE stream (same feed the relay gets, pre-encryption)
POST /v1/profile/claims    add/edit verified claims (re-runs Gate on in-flight items)
```
The bundled localhost dashboard and the tray app are both pure clients of this API — one implementation of truth.

---

## Part 8 — Security, Privacy & Trust

**8.1 Threat model headline:** this product reads email, holds OAuth tokens, and acts in the user's name — it must be engineered like a password manager, marketed like one, and audited like one.

**8.2 Secrets & scopes.** DPAPI-encrypted vault; incremental OAuth (never request `gmail.send` for an L1 user); refresh tokens revocable from a "Disconnect everything" button that actually calls the revocation endpoints. Google OAuth verification + restricted-scope security assessment is on the launch critical path (Gmail restricted scopes require it) — budget 6–10 weeks of calendar time.

**8.3 Pairing & E2E.** QR code on the PC encodes a one-time X25519 exchange; derived symmetric keys never touch the relay. Re-pair invalidates old phone keys. Relay stores ciphertext with 30-day TTL.

**8.4 Tamper-evident audit.** The hash-chained `events` table plus the Gmail Sent folder give the user two independent records of everything done in their name. "Show me everything you've ever sent" is a one-click export (PDF + JSON).

**8.5 PII discipline.** The engine never transmits SSN/DOB/financial data — these are unparseable by policy (red-flag scorer treats *requests* for them pre-offer as scam signals). Resume content goes only to the user-chosen LLM endpoint; Local-max mode keeps it on-device for most stages. No telemetry by default; opt-in crash reports are scrubbed.

**8.6 Honesty posture (product-level).** Nothing the engine sends pretends to be hand-typed-at-this-moment, but it also doesn't watermark every email "sent by AI" — the standard is the same as a human assistant or a mail-merge: real identity, real mailbox, user-authorized content, user-reviewable record. The line we never cross: novel substantive statements in the user's voice without the user's approval (§2.1, rule 2). This is both the ethical position and the legal-exposure position.

---

## Part 9 — What makes this design ours (novel contributions)

1. **The Approval Gate as the product, not a speed bump.** Everyone else chose between "human at the keyboard" and "spray blind." Relocating the human checkpoint to a 10-second push notification is the unlock that makes true background autonomy compatible with zero unreviewed actions — and it's exactly what the paid app sells.
2. **The Autonomy Contract.** Autonomy as an explicit, signed, revocable, parameterized agreement (caps, windows, answer bank, blocklist seeded with the user's current employer) rather than a settings page nobody reads.
3. **The Fabrication Gate as an unbypassable pure function** with the `claims` table as its oracle and confidence-tagged truth gathered *at onboarding* — fabrication prevention designed in, not patched in (learning from job-hunter's shipped bug).
4. **Two-axis scoring with a scam floor** that can block dispatch outright — autonomous agents need a "may not act" tier, not just a ranking.
5. **Local-first, relay-blind sync** — consumer-grade real-time dashboard with password-manager-grade privacy story.
6. **The Sunday Strategy Review** — the engine reports to its human like a contractor, closing the strategy-drift hole in every "set and forget" design.
7. **Channel-honest dispatch** — ATS-direct and email-native automation where it's welcome; everything-but-submit assistance where it isn't; never burning users' accounts on hostile boards.
8. **Story Bank + opt-in learning loop carried into a consumer surface** — the job search leaves the user *more* prepared for interviews than when it started.

---

## Part 10 — Build Plan

**MVP (v0.1, ~8–10 weeks of focused work):** engine service + tray + onboarding Phases A–C; Scout (Greenhouse/Lever/Ashby) + Scorer + Tailor + **Verifier** (the Gate ships in v0.1 or nothing ships); **L1 Drafts mode only**, Gmail drafts + daily digest; localhost dashboard; SQLite/event log. *This alone is a launchable, safe, genuinely useful product.*

**v0.2:** relay + apk read-only dashboard + push status. First Play Store release.
**v0.3:** L2 gates (push approvals), Dispatcher form-fill on the big-three ATSs, Correspondent template-class replies, follow-up cadence.
**v0.4:** Schedulist + Calendar, interview-day cards, Librarian lessons + Story Bank + Sunday Review.
**v0.5:** L3 rails mode, Local-max LLM mode, plugin SDK, managed-inference Pro tier.

**Launch-blocking checklist:** public code signing · Google OAuth restricted-scope verification · Play Store data-safety disclosures · legal review of the Autonomy Contract language and per-jurisdiction automated-correspondence rules · red-team pass on the Fabrication Gate and the prompt-injection surface (a job posting is untrusted input that flows into an LLM with send-email tools — JD content must be strictly quarantined from instruction context, and the Gate + Answer Bank are the containment).

**KPIs that define "amazing job," not "many applications":** interview-per-application rate (target ≥ 2× the user's manual baseline) · fabrication-gate escapes (target: zero, ever) · gate-approval latency (median < 1 h) · ghost-job dispatch rate (target: zero) · time-to-first-interview · user-reported offer quality.

---

*Fin. The engine hunts; the human commands; the record is permanent. LFB.*
