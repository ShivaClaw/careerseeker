# CareerSeeker — Dual-Repo Critical Audit
**Date:** 2026-07-13 · **Auditor:** Claude (Anthropic) · **Method:** cloned both repos; static read of all docs + targeted source; empirical harness execution in sandbox; full-history secrets scan
**Scope:** `ShivaClaw/careerseeker` @ HEAD `3fa65f5` · `ShivaClaw/ShivaClaw.github.io` @ `1f59084`
**Note on method:** one model ran six sequential lens passes (provenance, Windows systems, security, Google compliance, Android/Play, product coherence). No literal subagent spawning is available in this environment; the structure below reflects those passes.

> Current-status note, 2026-07-20: this is preserved as historical audit input, not as current status for
> this branch. Several findings below are now closed or materially changed on PR `#1` / branch
> `agent/repo-cleanup`: CI is present and green; `GatewayGateHarness` and `DispatcherNoSendHarness` are in the
> offline verifier; the default verifier reports 265 passed / 0 failed; live Scout, BYOK Tailor/Gate, Brave
> research, package export/import, and draft-only Gmail paths have fresh evidence in
> `docs/External-Audit-Handoff.md`. Treat the finding text below as dated evidence from commit `3fa65f5`
> unless the current handoff or current source confirms it still applies.

---

## 0. Verified-good (evidence, not vibes)

Before the problems, what checked out — because after the Codex incident, positive claims need receipts too:

- **HEAD is `3fa65f5`**, five commits past the documented `2066213` baseline: `bb5b3f2` (.mf), `a6e575f` (Opus audit), `a085935` (draft-ID scrub), `1348f50` (concurrency + injection quarantine), `dfb2932` (docs-site), `3fa65f5` (SQLite restore).
- **The SQLite restoration is real this time.** `Store.csproj` has `<PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.11"/>` and no `<Compile Remove>`; `nuget.config` lists nuget.org; `tests/StoreParityHarness/` exists and is in the `.sln`. Every item the 07-08 audit refuted is now genuinely on `main`.
- **Invariants hold in source.** `Stages.cs:68` pins `VerifierEntailment` in `PinnedStages`; `Budget.cs:55` exempts pinned stages from throttle; `Routing.cs:42` throws `PinnedDowngradeException` rather than downgrading; `Gateway.cs:94` fails closed with `NoProviderException`. `IGmailDraftClient` (`Dispatch.cs:68`) exposes `CreateDraftAsync` + `EnsureLabelAsync` — **no send method**. `Host.cs:77` binds `http://localhost:{port}/` (loopback only).
- **Empirical harness run** (this sandbox blocks `api.nuget.org`, so I built a clean copy at `/tmp/shipcheck-audit` with the original `<clear/>` nuget.config and Sqlite excluded — sandbox-only patch, disclosed): **Slice 12/12 · EngineHarness 13/13 · ResearcherHarness 21/21 · HookHarness 10/10.** StoreParityHarness needs the NuGet package and couldn't run here; run it on the Windows box.
- **No secrets** in the working tree or anywhere in git history (pattern scan: Google API keys, `sk-ant-`, private keys, literal refresh tokens / client secrets). `.gitignore` covers `client_secret.json`.
- **Site plumbing:** CNAME → `careerseeker.app`; `/`, `/privacy/`, `/support/` all present.

---

## 1. Critical — decide/fix before the next build phase

### C1. Two harness suites you believe exist do not exist on `main`
`GatewayGateHarness` (21 assertions) and `DispatcherNoSendHarness` (5) are referenced **nowhere** — not in `tests/`, not in the `.sln`, not in any doc on `main`. The working belief that they pass is a claimed-vs-actual gap of exactly the class you've been burned by twice. Current offline coverage of the two headline invariants is thin: EngineHarness #14 (`pinned verifier stage proceeds over cap`) covers only the budget exemption — not the fail-closed `NoProviderException` path nor the `PinnedDowngradeException` local-max path — and #15 (`L1 SubmitAsync throws NotSupportedException`) covers the dispatcher submit stub, while the `.mf`'s own recommendation ("add 'no send method present' reflection assertion to offline tests") remains unimplemented.
**Fix:** recreate both suites: (a) Gateway — assert pinned stage survives budget cap, throws on local-max downgrade attempt, throws `NoProviderException` (not local fallback) when StrongCloud is empty, and that removing one strong vendor still serves the Gate; (b) Dispatcher — reflection assertion that `IGmailDraftClient` has no member matching `Send*`, plus body==Gate-cleared-cover-letter verbatim check. Cheap (BCL-only), high leverage.

### C2. No CI — the structural mitigation for your #1 process risk is missing
SHA-verification is currently a human ritual. A ~30-line GitHub Actions workflow (`dotnet build` + run the five offline harnesses on every push/PR to `main`) converts "trust the agent's claim" into "the badge is green or it isn't." Now that `nuget.config` points at nuget.org, this works out of the box on `ubuntu-latest`/`windows-latest`. This is *structural over policy* applied to your own process; do it before the next Codex session, and add a check that fails if `docs/CareerSeeker-Project-Summary.mf`'s `main_commit` doesn't match the commit being built (see H4).

### C3. "Managed inference (default)" contradicts `no_hosted_pipeline` and your live privacy copy
The 5.6 addendum makes **Managed** — "our proxy with metered quota" — the onboarding **default**. But the `.mf` founding decisions say `no_hosted_pipeline` with the blind relay as the *only* planned server, and the live privacy page says "CareerSeeker does not run the application pipeline on CareerSeeker servers." An inference proxy is a server that *must* see resume claims, job text, and pre-Gmail draft text in plaintext — it cannot be blind. Three coherent resolutions; pick one on paper before B2 wiring bakes a default in:
1. **BYOK/Local default, Managed as opt-in upsell** — preserves doctrine and copy; costs onboarding smoothness the addendum was designed to buy.
2. **Managed default with explicit disclosure** — amend founding decisions + privacy copy ("pipeline runs locally; inference requests are proxied through CareerSeeker unless you supply your own key"); accept the weaker "local-first" marketing line.
3. **Managed = metered *keys*, not a proxy** — you provision per-user provider keys with quotas; calls still go device→provider direct; you never see content. Harder billing/abuse story, but it's the only version of Managed that keeps the trust posture intact. Worth serious consideration.
Whatever you choose, the .mf, spec 5.6, and privacy copy must say the same thing.

### C4. Windows Service + "headed-but-minimized" Playwright is a Session-0 contradiction
Spec 5.1: `SeekerSvc.exe` is a Windows Service (survives logoff, 3 AM runs) *and* Playwright runs "headed-but-minimized … human-pace input." Services live in Session 0 — no interactive desktop, so no headed browser. And headless mode is exactly what ATS anti-bot heuristics flag, which the headed choice was meant to avoid. Options:
- **Split the work:** service does Scout/Score/Tailor/Verify/Drafts (no browser needed for L1); form-fill (L2+, B4-adjacent) runs in the **per-user** tray process or a per-user Scheduled Task, i.e., browser work only happens while a user session exists. Honest tradeoff: no 3 AM form-fills — arguably a feature (human-plausible hours).
- Or accept headless + stealth hardening and drop the "headed" claim.
Related, decide now because tokens are involved: spec 5.2 says DPAPI "scoped to the service account," but the working GmailLiveHarness vault is **user-scoped** DPAPI. Service-account-scoped secrets can't be read by the per-user tray and vice versa; you need an explicit owner (recommend: user-scope, per-user service or task) plus a Playwright browser install location that isn't `%LOCALAPPDATA%\ms-playwright` of the wrong identity.

### C5. The MVP promises a "daily digest email" that L1 structurally cannot send
Part 10 MVP = "L1 Drafts mode only, Gmail drafts + daily digest," and §2.2 says L2 gates are approvable "via magic links" in that digest. L1 is `gmail.compose` with **no send path** — the engine cannot email anyone, including its own user. Magic links additionally require an internet-reachable endpoint (the engine is behind NAT), i.e., the relay terminating approval clicks. Redefine L1 digest as: tray/toast notification + localhost dashboard summary page (already built) + optionally a *draft* addressed to self. Email digest + magic links move to L2/relay scope, where their send-path and endpoint costs are priced in.

---

## 2. High

### H1. The live privacy policy is a regression of the copy you already wrote — and Google will read the live one
All three `docs-site/` pages **differ** from the deployed site. Direction matters: `docs-site/privacy.html` (committed 07-08) contains effective-date fields, a data table, a revocation section, "send capability does not exist in the application, not merely disabled," and a full **Third-Party LLM Inference** section (BYOK, what data is sent, "full resume is never sent in a single request"). The live page (07-10, *newer*) dropped all of it. Additionally, **neither** copy contains the Google API Services User Data Policy / **Limited Use** language, including the now-expected affirmative statement that Google user data is not used to train generalized AI/ML models — reviewers check for this on restricted-scope apps. And `docs-site/autonomy-contract.html` was never deployed.
**Fix:** treat `careerseeker/docs-site/` as the single source; port it to the Pages repo (or point Pages at it); add a Limited Use section; fill the `[DATE]` placeholders; deploy the Autonomy Contract page. Then delete or stub the third copy (`docs/Privacy-Policy.md` etc.) so there are not three diverging truths (see H3).

### H2. Spec 7.3 local API has no authentication — and two of its endpoints are one drive-by webpage from disaster
`POST /v1/gates/{id}` (approve applications) and `POST /v1/control` (pause/kill) on `127.0.0.1:7777` with no auth are CSRF-reachable: any webpage can fire cross-origin form POSTs at localhost without a CORS preflight, and any local process or other Windows user on the machine can hit them directly. A malicious job-posting page auto-approving gates is exactly the prompt-injection-adjacent scenario your Scout treats JDs as untrusted for. Today's `Host.cs` serves only read-only GETs, so current exposure is low — which is why the fix belongs in the spec **now**, before gates/control are implemented: per-install bearer token (stored in the vault, injected into tray/dashboard), require `Content-Type: application/json`, validate `Host`/`Origin`, bind `127.0.0.1` explicitly. Five lines in §7.3.

### H3. Two repos + three copies of the trust copy already diverged in 48 hours
`docs/*.md` placeholders, `docs-site/*.html`, and the live Pages repo are three sources for the same legal/marketing text; H1 is the proof it's already bitten. Pick one canonical location (recommend `careerseeker/docs-site/`) and make deployment mechanical (Action that pushes to the Pages repo, or move Pages hosting into `careerseeker` itself once it's private again — GitHub Pages from a private repo requires a paid plan, which may itself decide the architecture).

### H4. The "living handoff" is five commits stale and self-contradictory
`.mf` lines 8/548 claim `main_commit: 2066213` while HEAD is `3fa65f5`; line 60 says nuget.config "clears package sources" while line 292 says Sqlite is "restored through nuget.org" — both can't be true and only 292 is. README lists four offline harnesses (StoreParityHarness missing), still references "per-module xUnit mirrors … in module `Tests/` folders of the original deliverables" that are not in this repo, and every doc says EngineHarness=11 while it is now **13**. Any agent consuming the .mf will act on a false map — the precise failure mode this file exists to prevent. **Fix:** rule that structural commits update the .mf in the same commit; CI check (C2) that `document.main_commit == $GITHUB_SHA` (or short form) or the build fails.

---

## 3. Medium

- **M1. `EnsureLabelAsync` on the L1 port is a policy guard on a structural doctrine.** Live testing proved labels 403 under `gmail.compose`; yet the compose-only interface still carries the method, "disabled by default." By the same logic that removed `Send`, split it: `IGmailDraftClient` (L1) loses `EnsureLabelAsync`; an `IGmailLabelManager` appears only in L2 builds. Then the L1 binary is structurally label-incapable, matching the README's promise.
- **M2. Stale EV-certificate instructions.** Spec 5.1, Roadmap A4, and the Part 10 checklist all say "EV code-signing cert"; the standing decision is Azure/Microsoft Trusted Signing (EV no longer buys SmartScreen bypass). Update all three so no agent spends ~$400 and weeks procuring the wrong thing.
- **M3. Spec §2.2 still sells "Gmail drafts/labels" in the free tier.** Labels are deferred; scrub the word or footnote it, or the marketing copy re-creates the scope-creep pressure you already killed.
- **M4. `%ProgramData%\CareerSeeker\seeker.db` is machine-global.** On a shared family PC, user B can read user A's entire job hunt (comp targets, employer blocklist, correspondence). Recommend per-user root (`%LOCALAPPDATA%`) with per-user service/task (pairs with C4), or at minimum per-user ACL'd subfolders. This is a privacy-product credibility issue, not a nicety.
- **M5. Gmail push "watch/webhook→relay" (§5.7) under-specifies its costs.** `users.watch` requires a Cloud Pub/Sub topic in *your* project and at least metadata-class read scope; the relay subscribing means it sees (email address, historyId, timing) — metadata, not content, but "blind relay" copy should say so, and L2's scope escalation (`gmail.readonly`/`metadata` + `gmail.send`) should be written down now so CASA Tier-2 scoping at launch isn't a surprise.
- **M6. The leaked draft ID persists.** `a085935` scrubbed the `.mf`, but `AUDIT-2026-07-08.md:25,140` still quote the real value verbatim in public history (`bb5b3f2`, `a6e575f`). The value is redacted from this audit. Inert without a token, it still violates `secret_policy` and came from a real test account. Treat the ID as burned, adopt "audits quote redacted forms," and assume anything present during the public window may have been crawled.
- **M7. Hardcoded model prices in `Routing.cs:60-67`.** Budget math silently rots as vendors reprice. Move prices (and model IDs) to a versioned constants block with a `pricing_as_of` date surfaced in the cost meter, or fold into the signed board-registry-style manifest already planned.

---

## 4. Low / housekeeping

- **L1. ".apk" framing:** Play has required **.aab** upload (Play App Signing) since 2021; users still receive APKs, but docs, build plan, and signing-key handling should say AAB. Colloquial ".apk" in marketing is fine.
- **L2. Future Pro billing:** a Play subscription whose main deliverable (managed inference) is consumed on Windows sits in Play-billing gray zone; when v0.5 nears, evaluate selling Pro on the web and keeping the app a one-time $4.99. Also note: a paid app can later become free, never the reverse.
- **L3. Provider-list drift:** 5.6 addendum says BYOK "Anthropic / OpenAI / Google"; routing table and .mf say Anthropic/Gemini. Pick one.
- **L4. Mail routing:** `support@` / `privacy@careerseeker.app` are load-bearing for OAuth verification — confirm Cloudflare Email Routing MX is live before submitting.
- **L5. Public-window notice:** repo has no LICENSE (default all-rights-reserved — correct for proprietary), but consider a one-line proprietary notice in README while it's public.

---

## 5. Recommended sequence (smallest set that unblocks everything)

1. Flip `careerseeker` private again (M6) and redact the AUDIT draft ID.
2. Add CI (C2) + recreate GatewayGate/DispatcherNoSend harnesses (C1) + .mf freshness check (H4). One PR.
3. Adjudicate C3 (managed proxy) and C4 (service/browser split) as ADRs — both are pen-and-paper decisions that gate B2/B4 respectively.
4. Fix the trust-copy pipeline (H1/H3): canonical docs-site → deployed site, Limited Use section added, Autonomy Contract deployed, third copy deleted.
5. Amend spec: §7.3 auth (H2), §2.2 labels/digest wording (C5/M3), §5.1/§5.2 packaging + DPAPI owner (C4), EV→Trusted Signing (M2), L2 scope map (M5).
6. On the Windows box: `dotnet run` StoreParityHarness (unverifiable from this sandbox) and record it in the .mf test log.

Nothing here questions the core: the Gate, the pin, the no-send port, and the scoring floor are real, in code, and passing. The findings are almost all *seams* — between docs and code, between repos, between L1 doctrine and L2 ambitions — which is exactly where this project has historically taken damage.

---

# ADDENDUM — 2026-07-13: Verification of GPT-5.6 counter-audit

Every checkable claim was verified against the clone at `3fa65f5` plus current Google/Anthropic documentation. Scorecard:

**Confirmed (9/10 checkable claims):**
1. **CS0128 — the pushed solution does not compile.** `tests/StoreParityHarness/Program.cs` uses top-level statements with two local functions named `AssertEqual` (lines 78 and 84). C# local functions cannot be overloaded. `dotnet build CareerSeeker.sln` fails at HEAD. My sandbox patch (excluding Sqlite/StoreParity because api.nuget.org is blocked here) masked precisely this path — a disclosed limitation, but the miss is real. Corollary: commit `3fa65f5` was pushed without a clean full-solution build — a new claimed-vs-actual instance.
2. **Decomposer gap.** Independent scan covers only `%`, `$`, `N years`, credential cues (`certified|licensed|AWS|PMP|…`), and amplified-skill phrases (`expert in|mastery of|…`) — `Decomposer.cs:16-26`. An undeclared "I served as CTO at Google" yields no atom and is never entailment-checked. Model under-declaration of **titles/employers** bypasses the Gate.
3. **Pinned stage not wired to the Gate.** Zero references to `VerifierEntailment` outside `src/Gateway`; `FabricationGate.Verify` defaults to `DefaultSemanticMatcher` (token overlap); `ISemanticMatcher.Entails` is **synchronous** (`Matchers.cs:73-76`), which structurally blocks an async provider-backed matcher. Nuance: `Matchers.cs` comments and the .mf both describe gateway NLI as pending B2 work, so this is a known-open seam, not concealment — but my §0 "invariants hold in source" framing implied an end-to-end pin that does not yet exist. Correction accepted.
4. **`gmail.compose` is send-capable.** Google's own scope constants describe it as "Manage drafts and send emails" (it permits `drafts.send`). The safeguard is real but app-level only: *the binary contains no send call*; the *token* can send. All wording claiming the permission itself cannot send (code comments, README, privacy copy, Autonomy Contract) is factually wrong and must be corrected — this also matters for consent-screen/CASA accuracy and for the honesty posture (§8.6).
5. **Concurrency remnants after `1348f50`.** `Budget.cs:32-34` reads `_spent` (a decimal — not atomic in .NET) outside `_spentLock`; `Accounting.cs:26` does an unsynchronized read-modify-write on `_total` (lost updates under concurrent `Record`).
6. **DRAFTED before the draft exists.** `RouteAsync` transitions to `RouteFromReady(L1)=DRAFTED` (`ApplicationPipeline.cs:118-119`, `Lifecycle.cs:95`) *before* `CreateDraftAsync` (`:124`). A Gmail failure strands a false DRAFTED state. Additionally `TransitionAsync` is validate-then-persist with no conditional update, so concurrent L2 approvals can double-submit — real final-exe hazards.
7. **Model registry already wrong.** `gemini-3-pro` was shut down 2026-03-09 (successor `gemini-3.1-pro-preview`, $2/$12); Claude Haiku 4.5 is **$1/$5**, not $0.25/$1.25 (that is legacy Claude 3 Haiku pricing) — a 4× undercount feeding the budget meter. The advertised cross-vendor StrongCloud failover would currently error; the Gate fails closed (correct behavior), but vendor plurality is presently fictional. Supersedes M7.
8. **XML quarantine is cosmetic.** `GatewayTailorModel.cs:71` interpolates job title/company unescaped into `<job_title>/<job_company>`; `GatewayDossierModel.cs:56` wraps untrusted document text unescaped. Attacker-controlled values can close tags and inject instructions. **Composite risk:** injection (8) → model emits undeclared title/employer fabrication → Decomposer misses it (2) → weak matcher (3) = a plausible injection-to-Gate-escape chain. This is now the top-severity technical finding.
9. **docs-site copy not deployable as-is.** Markdown table residue and `[DATE]` placeholders confirmed; and its claim that only *posting-relevant* claims go to providers is false — no relevance filter exists anywhere in `src/` (grep: zero hits). H1's "port it" becomes "rewrite it, then port it."
10. **Manifest-SHA CI check impossible as written.** Conceded — a committed file cannot contain its own commit SHA. Replace H4's mechanism with: CI fails if designated structural paths change without a same-commit .mf change; .mf records the parent/base SHA.

**Unverifiable from this environment (verify on the Windows box):** local worktree at `bb5b3f2` with untracked harnesses (plausible — would explain where GatewayGate/DispatcherNoSend live; protect it: `git status`, branch/stash before any pull); absent MX for careerseeker.app (consistent with L4; check Cloudflare Email Routing); `/autonomy-contract/` 404 (consistent with the never-deployed finding).

**Revised sequence (merges both audits):**
1. On the Windows box: snapshot the dirty worktree to a branch before any pull/rebase; reconcile against `origin/main`.
2. Fix CS0128; restore a clean `dotnet build CareerSeeker.sln`; run StoreParityHarness; commit.
3. Close the injection→Gate chain: escape/strip `<>` at every prompt-assembly seam (one shared quarantine helper); extend the Decomposer with a title/employer sentence scan (or default-deny novel sentences containing org-like proper nouns absent from source claims); convert `ISemanticMatcher` to async and wire a `GatewaySemanticMatcher` through `Stage.VerifierEntailment` (this *is* B2's Gate wiring).
4. Correct the `gmail.compose` language everywhere; the honest formulation is "no send implementation exists in the app," never "the permission cannot send."
5. Lifecycle atomicity: transition to DRAFTED only after `CreateDraftAsync` succeeds (or add DRAFTING + compensating transition); add expected-from conditional transitions and submission idempotency keys; fix the Budget/Accounting synchronization.
6. Refresh the model registry (`gemini-3.1-pro-preview` or a stable successor; Haiku at $1/$5) and live-smoke real cross-vendor failover under B2.
7. CI + the two safety harnesses (C1/C2), with the corrected .mf-freshness rule.
8. ADRs: managed inference (C3), service/user-session identity (C4).
9. Rewrite → deploy trust copy (Limited Use section + accurate scope/relevance language), configure MX, return the repo to private.
