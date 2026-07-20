### 5.6 LLM Gateway *(amended — replaces v0.9 §5.6)*

**Design goal.** Keep the per-stage routing principle (cheap stages run on cheap models, the few expensive stages run on strong ones), but fix four things the v0.9 draft left loose: (1) onboarding should present **one** default, not a three-way fork; (2) tier placeholders ("Haiku-class") should resolve to **concrete, current models** with defensible cost; (3) no single vendor should be able to take the whole product down with one ban or price change; and (4) the Fabrication Gate's entailment check must be **structurally exempt** from any cost-saving downgrade.

#### Per-stage routing table (concrete defaults — the *class* is the contract, the model is swappable)

| Stage | Capability class | Default model (Jun 2026) | Approx cost | Notes |
|---|---|---|---|---|
| classify / dedup / extract | on-device small | Phi-/Llama-class via `llama.cpp` (CPU-ok) | ~free | cloud fallback: Flash-Lite |
| quick score (title + snippet) | cheap cloud | **Gemini 2.5 Flash-Lite** ($0.10/$0.40 per 1M) | ~$0.0001–0.0005/job | local fallback in Local-max |
| full evaluation + dossier delta | mid cloud | Gemini 2.5/3 Flash *or* Claude Haiku-class | ~$0.01–0.02/job | 6-block eval; multiple calls |
| tailoring + correspondence | strong cloud | Claude Sonnet-class *or* Gemini 3 Pro | ~$0.05–0.15/app | voice card + dossier hook applied |
| **Verifier entailment (the Gate)** | **strong cloud — pinned** | Claude Sonnet-class (or better) | ~$0.01–0.03/app | **never throttled, never downgraded** |

**The pinned Gate row is the one non-negotiable.** Decomposing tailored output into atomic claims and NLI-checking each against the `claims` oracle is the single safety-critical judgment in the system, and "fabrication-gate escapes: zero, ever" is a launch KPI. Budget throttle may pause discovery breadth, defer dossier refreshes, or coarsen quick-scoring — it may **never** route the entailment check to a cheaper model. Running the Gate on a budget model trades the one number that must stay at zero for a few cents. Disallowed in code, asserted in the golden tests alongside the no-bypass-parameter check.

**Vendor plurality, on purpose.** Each tier names a default, but a provider-abstraction layer sits under the routing table so a Google outage, an abuse-flag, or a price change swaps the model for a class without touching pipeline code. Flash-Lite is the default cheap-tier workhorse because it is currently the cheapest cloud option, not because we are married to Google. Email identity, inference, and sync deliberately do **not** all terminate at one vendor — concentrating them is how one policy change nukes the product.

#### Three modes, one visible default

- **Managed *(default)*** — onboarding shows a single line: *"Inference: CareerSeeker handles it."* Ships with a **starter credit** (≈ first two weeks / first N tailored applications, free). When the credit runs low the engine surfaces exactly one choice — *add your own key (free)* or *upgrade to Pro* — instead of forcing a mode decision at minute three of the wizard.
- **BYOK *(advanced)*** — behind an "Advanced inference" expander. User's Anthropic / Gemini (Google) key lives in the DPAPI vault; calls go direct to the provider; genuinely **$0 to us**. The hacker tier.
- **Local-max *(advanced)*** — everything that can run on-device does; tailoring quality degrades gracefully and the UI says so plainly. This is the *"your resume never leaves your machine for ~80% of the pipeline"* privacy path.

#### Who pays — the honest version

A full-throughput **L1 Drafts** week costs roughly **$2–3 of inference** (~$8–12/month) — real money that cannot be free forever (§2.2). So the economics are stated plainly rather than hidden:

- The Managed **starter credit is the on-ramp, not the destination.**
- Sustainable Managed = the future **CareerSeeker Pro** ($9–15/mo, unlimited inference + apk bundled).
- Genuinely-free-to-us paths remain real and one expander away: **BYOK** (user's own bill) and **Local-max** (CPU only).
- Hard monthly budget cap with engine auto-throttle stays; per-stage token accounting surfaces in analytics (*"this week: $1.84 / 312 calls"*). **Throttle order:** discovery/scoring breadth first, then dossier freshness — never the Gate, never an in-flight tailored application's verification.

#### Changelog vs v0.9

1. Concrete model defaults replace abstract "-class" placeholders, with current per-1M pricing.
2. Flash-Lite pinned as the cheap-tier default (currently cheapest cloud); routing tier kept abstract so it can be swapped.
3. **Managed** promoted to the single onboarding default; **BYOK** and **Local-max** moved behind an "Advanced" expander — the simpler onboarding, without hiding the cost.
4. New **pinned Verifier-entailment row**: the Gate is exempt from budget downgrade by construction.
5. Provider-abstraction note added to prevent single-vendor lock-in across email + inference + sync.
