# CareerSeeker Researcher / Dossier

The Researcher is the cached company-dossier builder. It turns web research into a structured dossier:
overview, signals, fit, risks, and the cover-letter hook. It also resolves the researched signals the
Scorer's legitimacy axis consumes.

## Safety Invariant

A dossier fact, above all a hook that lands in a cover letter, must be grounded in a retrieved source or
it is never emitted. This is the research-layer analogue of the Fabrication Gate, and like the Gate it is
enforced in code:

- The dossier model only proposes facts, each with a claimed source URL.
- `GroundingFilter` keeps a fact only if its URL was actually retrieved and the cited document text
  supports the wording.
- A model that invents a flattering hook or cites a URL that was not retrieved gets that fact dropped.
- Signals are positive-only: `Signals.Derive` returns `true` when established and `null` otherwise.

Retrieved document text is treated as untrusted data, never instructions.

## Flow

`BuildAsync(company)`:

1. Serve a fresh cached dossier if one exists within the TTL; `forceRefresh` bypasses cache.
2. Web search across overview, funding, and careers queries through `IWebResearch`.
3. The dossier model proposes facts from the docs.
4. `GroundingFilter` keeps only source-backed facts and exposes the dropped count.
5. `Signals.Derive` resolves recruiter-identifiable and domain-verified signals from retrieved docs.
6. Assemble a content-addressed dossier and cache it.

## Integration Seams

- `IWebResearch`: search + fetch. Real implementation: `BraveSearchWebResearch`; tests use fakes.
- `IDossierModel`: proposes facts. Real implementation: `GatewayDossierModel` over the LLM Gateway.
- `IDossierStore`: dossier cache. `InMemoryDossierStore` here; disk persistence remains production work.
- Scorer: `ResearchedSignals` map onto `JobPosting.RecruiterIdentifiable` and `CompanyDomainVerified`.
- Tailor: `Dossier.BestHook` is the one grounded company-specific line for the cover letter.

## Files

- `Dossier.cs`: dossier types and ports.
- `Grounding.cs`: grounded-or-dropped invariant.
- `Signals.cs`: deterministic researched-signal derivation.
- `Researcher.cs`: orchestrator, options, and in-memory cache.
- `GatewayDossierModel.cs`: `IDossierModel` over `Stage.FullEvaluation`.
- `BraveSearchWebResearch.cs`: Brave Search API adapter that fetches result pages before returning docs.
- `tests/ResearcherHarness`: offline plain-assertion runner.

## Verified Status

- Compiles clean against the Gateway: `dotnet build -c Release` returns 0 warnings, 0 errors.
- `ResearcherHarness`: 30 passed, 0 failed.
- Coverage includes the grounding invariant, positive-only signals, cache behavior, Gateway model bridge,
  dossier-to-Scorer seam, and the Brave adapter's auth/query shape, public-page fetch, HTML stripping,
  localhost refusal, non-text skipping, wrapper-shaped live model responses, deterministic source fallback,
  and research observability.
- Live `research-company` is verified with Brave Search plus BYOK dossier modeling. The command accepts
  `BRAVE_SEARCH_API_KEY`, `BRAVE_SEARCH_API`, or `CAREERSEEKER_BRAVE_SEARCH_API_KEY` from the environment
  or `secrets/env.secrets`.
- `scripts/Verify-Alpha.ps1 -IncludeResearch` repeats the live GitLab research smoke and fails if no
  grounded facts are returned.
