# CareerSeeker — Researcher / dossier (C# / .NET 8)

The Researcher (spec §5.4) is the cached company-dossier builder. It turns web research into a structured
dossier — overview, signals, fit, risks, and the cover-letter **hook** — and resolves the researched
signals the Scorer's legitimacy axis consumes. `net8.0`, BCL-only; references the LLM Gateway for the
dossier-model bridge.

## The safety invariant: grounded or dropped

A dossier fact — above all a **hook**, which lands in a cover letter the user *sends* — must be grounded
in a retrieved source, or it is never emitted. This is the research-layer analogue of the Fabrication
Gate, and like the Gate it is enforced in code, not asked of the model:

- The dossier model only ever **proposes** facts, each with a claimed source URL.
- `GroundingFilter` keeps a fact only if its URL was actually retrieved **and** the cited document's text
  supports the wording (distinctive-token overlap). A model that invents "congrats on your IPO" or
  misattributes a fact to a real URL gets it dropped.
- Membership is positive-only for signals too: `Signals.Derive` returns `true` when established and
  `null` (unknown, scored neutral) otherwise — research can raise confidence, never manufacture doubt,
  and never moves the legitimacy axis on a hallucination because it is fully deterministic.

Retrieved document text is treated as **untrusted data** (prompt-injection risk, like Scout's JDs), never
as instructions.

## Flow

`BuildAsync(company)`:
1. Serve a fresh cached dossier if one exists within the TTL (default 14 days) — `forceRefresh` bypasses.
2. Web search across overview / funding / careers queries (`IWebResearch`), deduped.
3. The dossier model proposes facts from the docs (`IDossierModel`, routed through the Gateway's
   `FullEvaluation` mid-cloud stage).
4. `GroundingFilter` keeps only source-backed facts; the rest are dropped (count exposed for observability).
5. `Signals.Derive` resolves recruiter-identifiable / domain-verified deterministically from the docs.
6. Assemble a content-addressed `Dossier` (hash over facts) and cache it.

## Integration seams

- **`IWebResearch`** — search + fetch. Real: a search API; sandbox: a fake batch. Output is untrusted.
- **`IDossierModel`** — proposes facts. Real: `GatewayDossierModel` over the LLM Gateway; sandbox: a fake.
- **`IDossierStore`** — dossier cache. `InMemoryDossierStore` here; disk content-addressed in production
  (spec §6: `dossier_path`, `dossier_at`).
- **Scorer**: `ResearchedSignals` map onto `JobPosting.RecruiterIdentifiable` / `CompanyDomainVerified`.
- **Tailor**: `Dossier.BestHook` is the one grounded company-specific line for the cover letter (spec
  §5.5). Wiring it into the Tailor prompt is a small Tailor-side addition (an optional hook on
  `TailorModelRequest`); the Researcher surfaces it grounded and ready.

## Files

- `Dossier.cs` — `DossierTopic`, `DossierFact`, `ResearchedSignals`, `Dossier`, and the ports.
- `Grounding.cs` — `GroundingFilter`: the grounded-or-dropped invariant.
- `Signals.cs` — deterministic researched-signal derivation.
- `Researcher.cs` — orchestrator + `ResearcherOptions` + `InMemoryDossierStore`.
- `GatewayDossierModel.cs` — `IDossierModel` over the Gateway's `FullEvaluation` stage.
- `Tests/Harness.cs` — offline plain-assertion runner.

## Verified status (this sandbox)

- Compiles clean against the Gateway: `dotnet build -c Release` → 0 warnings, 0 errors.
- **21 scenarios pass**: the grounding invariant (real fact kept; hallucination, unretrieved-URL, and
  sourceless facts all dropped), signals derivation (positive-only), caching (second build served from
  cache; `forceRefresh` re-runs), the full bridge path through the Gateway's `FullEvaluation` stage, and
  the dossier→Scorer seam (grounded signals raise legitimacy versus unknown).
- The real `IWebResearch` runs at integration (network); the model path is exercised here against a
  deterministic provider.
