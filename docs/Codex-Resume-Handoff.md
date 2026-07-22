# Codex Resume Handoff

Updated: 2026-07-22

## 2026-07-22 (Opus session) — publish-to-web roadmap, phases W0–W3 (blocked at W1 on R2)

Executing the 60-hour alpha publish roadmap (`Alpha-Publish-Roadmap-2026-07-22.md`, Fable 5) toward a
tester-downloadable alpha on `careerseeker.app`. Never trust a SHA here — re-derive.

**W0 — consolidation (done).** `claude/alpha-finish` fast-forwarded onto the F2 branch
(`claude/codex-audit-pr2-triage-mjdur6`) and pushed, so PR #4 is now the single complete Friday diff
(checkpoint → H1/H2/H3 → A1/L1/M1/M2 → F2). **The F2 claim is now verified on Windows**, closing the
"not executed here" caveat in the Fable section below: `dotnet build -c Release --warnaserror` 0W/0E,
and the full `scripts/Verify-Alpha.ps1` prints `Offline total: 327 passed, 0 failed` with the measured
total equal to the pinned `$ExpectedOfflineTotal`. The packaged path Fable could not exercise on Linux
is green too: `-IncludePublish -IncludePackage` gave publish smoke `errors: 0`, `manifest: ok`,
`checksums: 46 verified`.

**Release candidate artifact (rehearsal build, from head `1d1a5a4`):**
`output/release/CareerSeeker-alpha-win-x64.zip`, 31,018,621 bytes,
SHA-256 `D8F4916F949E225E87B3FB4B8D09A6FEF50DC7F2B68E0E19ED0BDC1CB981C7C7`.
This is the **rehearsal** artifact for infra testing only — the tester-facing ZIP is rebuilt from the
merged line on Friday and its hash replaces this one everywhere.

**Trap worth recording:** the first `-IncludePackage` run failed with *"Alpha release manifest was
generated from a dirty working tree"*. Cause was an untracked, **non-gitignored** `careerseeker-site-v2.zip`
sitting in the repo root (a site snapshot, not repo content). Moved to
`Desktop/careerseeker-site-v2-snapshot-2026-07-21.zip`; the run then passed. The packaging step requires a
clean tree, and `*.zip` is not gitignored at the repo root — worth adding to `.gitignore` so this cannot
recur or, worse, get committed.

**W1 — distribution infrastructure: BLOCKED, needs Brandon.** The roadmap assumed the only risk was
token scope. It is worse than that:
- `CLOUDFLARE_ACCOUNT_API_TOKEN` is valid (it lists the `careerseeker-site` Pages project fine) but has
  **no R2 permission** — `wrangler r2 bucket list` fails with API error **10000** (authentication).
- An independent credential path (Cloudflare MCP, separate OAuth) authenticates but fails with error
  **10042: "Please enable R2 through the Cloudflare Dashboard."**

So **R2 is not enabled on the account at all** — this is a one-time account-level product activation
(dashboard → R2), not something a token grant can fix, and not something an agent should do on the
owner's behalf. Until R2 is enabled *and* a token with R2 read+write exists, W1.2 (bucket + upload),
W1.4 (`RELEASES` binding), and W1.5 (prove the pipe) cannot run. Roadmap §2's GitHub-Releases fallback
remains available and needs only a one-line change to the serving Function (302 redirect).

**Site changes made (live only in `C:\Users\bkirk\Desktop\site-v2`, still NOT under version control —
strongly recommend git-initializing it).** Backup taken first at `Desktop/site-v2-backup-2026-07-22`.
- New `functions/releases/[[path]].js` — streams from the `RELEASES` R2 binding under the `alpha/`
  prefix, flat-filename regex (no traversal), `Content-Disposition: attachment`, `nosniff`. Added one
  guard beyond the roadmap's listing: a missing `env.RELEASES` returns 404 rather than throwing a 500
  with a stack — which is the *current* state, since the binding does not exist yet.
- `functions/api/verify.js` — `download_url` now
  `https://careerseeker.app/releases/CareerSeeker-alpha-win-x64.zip` (the dead `CareerSeeker-Alpha-Setup.exe`
  TODO is gone). This URL is identical under both the R2 and GitHub-Releases paths, so it is not blocked.
- `download/index.html` — rewritten: ZIP contents, tester quickstart, the draft-only invariant stated
  plainly, SHA-256 in a `<code>` block marked `<!-- SHA-UPDATED-F2.3 -->`, and unsigned-binary/SmartScreen
  guidance. Sole CTA is still "Request alpha access" → `/beta/`; the raw file URL stays unpublished.
**Deployed to production** (Brandon authorized production deploys explicitly; note the roadmap's W1.5
command targets **production**, not preview — `--branch <name>` is what gives a preview). Deploy
`b656a582`, run from inside `site-v2`, printed both required lines — `Compiled Worker successfully` and
`Uploading Functions bundle` — so Functions shipped. Verified against both the per-deploy URL and
`https://careerseeker.app`:

| Check | Result |
| --- | --- |
| `/releases/CareerSeeker-alpha-win-x64.zip` | 404, body exactly `Not found.` — the Function's own body, **not** a 500 |
| `/releases/..%2fsecrets`, `/releases/nested/path.zip` | 404 (path hygiene holds) |
| `POST /api/verify` bad code | 403 `{"error":"Invalid code…"}` — **BETA_KV survived adding the new Function** |
| control: `/definitely-not-a-real-page-xyz` | serves the site index — i.e. Pages' default 404, proving the `/releases/` 404 above really is the Function |

This is all of W1.5 except the R2 round-trip hash. Deploying before R2 exists was deliberate and carries
no regression: BETA_KV is **empty** (zero signups, zero issued codes — checked before deploying), and the
`download_url` 404s exactly as the old `Setup.exe` URL did. It proves the Function compiles and ships now
rather than on go-live day. The `!env.RELEASES` guard is confirmed working in production — the binding is
genuinely absent and the route 404s cleanly.

**Per-deploy URLs lag.** The first verification pass against `b656a582.careerseeker-site.pages.dev`
returned Cloudflare's *"Deployment Not Found"* page, which is a 404 that looks exactly like a real one.
Re-probe after ~30 s and check the **body**, not just the status code, before concluding anything is
broken. (The existing note about the `preview.` alias lagging applies to the hash URL too.)

**W3.1 clean-machine rehearsal — done, green.** Fresh extract of the ZIP to `%TEMP%`, then:
`Verify-CareerSeeker-Alpha.cmd` → `manifest: ok`, `checksums: 46 verified`, `dashboard smoke: passed`;
`Setup-CareerSeeker-Alpha.cmd` → workspace, `profile.template.json`, `secrets/env.secrets` all created;
`Run-CareerSeeker-Demo.cmd` → `errors: 0` (cycles 1, discovered 3, acted 1, drafted 1, blocked 1,
rejected 1); dashboard → `/` and `/evidence.html` both HTTP 200 carrying `no-store` / `nosniff` /
`no-referrer`. No credits spent, no Gmail, no network.

Friction list from the rehearsal (tester-facing, small):
1. **`Setup-CareerSeeker-Alpha.cmd` lists the demo as step 6** — behind profile editing, API keys, Gmail
   OAuth, and live-readiness. The demo needs none of those. A tester following that order hits three
   credential chores before seeing the product work. Reorder so the demo is step 1. (Site copy already
   says "run the demo first", so the launcher currently contradicts the download page.)
2. Setup step 3 tells the tester to hand-edit `secrets\env.secrets` in Notepad to add API keys. That is
   the roughest edge in the flow and undercuts the "the app walks you through setup" promise.
3. Setup spawns Notepad on `profile.template.json` and the launchers end in `pause` — both correct for a
   double-clicking tester, but they make headless/CI invocation hang. Not a tester defect; noted so the
   next session does not mistake it for one.

**Not yet done:** W1.2 (bucket + upload) and W1.4 (`RELEASES` binding) — both blocked on R2 being
enabled; the R2 round-trip hash check from W1.5; W2.3 beta-flow rehearsal and W3.2 SmartScreen
observation, which both need a download that actually serves; W4 live verification (Gate B, not
approved); F1/F2 (Friday). Gates C1/C2 remain Brandon's.

**The moment R2 is enabled**, the remaining work is short and unblocked: create bucket
`careerseeker-releases`, upload the ZIP under `alpha/`, add the `RELEASES` binding to **both**
production and preview (send the *merged* Pages config — a PATCH that omits `BETA_KV` breaks the beta
API), redeploy, then re-run the table above expecting a 200 whose SHA-256 equals the recorded hash.

## 2026-07-22 (Codex-role audit, Fable 5) — PR #2/#4 triage + one confirmed fix

Never trust a SHA in this file — derive with `git rev-parse --short HEAD` / `git log --oneline -8`.
This session ran the Phase-2 audit-support role (Codex active) against the consolidated post-checkpoint
diff (PR #4 `claude/alpha-finish` → `agent/repo-cleanup`, which carries PR #2's H1/H2/H3 + CLAUDE.md and
PR #3's A1/L1/M1/M2). Work landed on branch `claude/codex-audit-pr2-triage-mjdur6`, based on the
`claude/alpha-finish` tip so the fix rides on top of everything under audit.

**Environment note:** audited on Linux with .NET 8; the offline harnesses are cross-platform and were run
directly. `scripts/Verify-Alpha.ps1` is Windows/PowerShell-oriented (workspace initializer, docs-site,
publish/package steps) and was **not** executed here; instead the two things it would catch were validated
directly — the measured offline total (327, matching `$ExpectedOfflineTotal`) and every count-bearing
doc-smoke assertion string. Re-run the full `Verify-Alpha.ps1` on Windows to confirm the packaged path.

Triage verdicts against source (confirm = claim holds; the PR bodies' self-disclosures all held up):
- **H1 connect-time guard** — CONFIRMED sound: fail-closed multi-address rule, dials the validated IP
  (no re-resolution TOCTOU), redirects re-enter the ConnectCallback. **But** its IP classifier had a real
  gap (see the fix below).
- **H2 sweep scope** (demo/alpha/dashboard swept; six one-shots unswept) — CONFIRMED correct.
- **Store parity** `GetApplicationIdsInStatesAsync` — CONFIRMED a pure read, zero `Now()` in both stores;
  parity case passes (StoreParity 22).
- **A1** (`::` rejected), **L1** (`PRAGMA table_info` migration; column index 1 is the name; idempotent,
  pre-existing row preserved, round-trips), **M1** (pinned `$ExpectedOfflineTotal` + drift throw; the
  premise correction that CI already runs the SQLite harnesses is right), **M2** (query-string doc token
  is a documented acceptance behind loopback + `RequestCameFromThisDashboard` + per-process token) — all
  CONFIRMED as described.
- **Verifier whitespace-normalized row assertions** — CONFIRMED robust.

**Confirmed finding fixed this session — F2 (SSRF classifier, IPv6 embedded-IPv4):**
`PrivateNetworkGuard.IsPubliclyRoutable` returned `true` for IPv6 forms that embed or route to a private/
loopback IPv4 — IPv4-compatible `::/96` (e.g. `::7f00:1` = 127.0.0.1, `::169.254.169.254`), NAT64
`64:ff9b::/96`, and 6to4 `2002::/16`. The guard already unwraps IPv4-*mapped* `::ffff:` for exactly this
reason and A1 had just closed `::`; these were the same family of gap left open. Fix reclassifies any such
address by the IPv4 it reaches (`TryExtractEmbeddedIPv4`), so a private v4 can no longer slip through in a
v6 disguise. Two harness cases added to the `[ SSRF guard ]` section (reject the private-embedding forms;
regression-guard that genuinely-public v6 and NAT64/6to4-wrapping-a-public-v4 stay routable).
ResearcherHarness 53→55, offline total 325→327; `$ExpectedOfflineTotal` and all five asserted doc counts
bumped in lockstep per the CLAUDE.md drift trap.

**Residual noted, NOT changed (needs a product decision — G6-adjacent):** the guarded `HttpClient` leaves
`SocketsHttpHandler.UseProxy` at its default (true), so if a system/environment HTTP proxy is configured
the `ConnectCallback` validates the *proxy's* address, not the redirect target — the connect-time IP guard
is bypassed for the real destination when a proxy is present. Confirmed by repro (the handler routed
through an injected env proxy). Low/situational for a local Windows alpha (the string pre-filter still
blocks literal-IP private targets). Forcing `UseProxy=false` would break testers who need a corporate
proxy for outbound internet, so this is left for Brandon/Opus to decide rather than changed in a triage
pass.

Verification (this session, on this branch): `dotnet build CareerSeeker.sln -c Release --warnaserror`
0W/0E; all nine offline harnesses green, measured total **327** (Slice 28 · Engine 89 · Researcher 55 ·
Hook 14 · StoreParity 22 · GatewayGate 34 · DispatcherNoSend 35 · Lifecycle 44 · Renderer 6), equal to the
pinned `$ExpectedOfflineTotal`. Invariants unchanged: no Gate bypass, `VerifierEntailment` pin untouched,
Dispatcher still no-send, local-first, reconcile side-effect-free. No secrets printed; no live/spending
runs (G2 intact); no Gmail draft created.

**Gate G1 (merge PR #2/#4 → `agent/repo-cleanup`) is unchanged and remains Brandon's call** — nothing was
merged this session. When G1 happens, re-derive the merged head with `git rev-parse --short HEAD` and
record it here (no embedded head, per H3).

## 2026-07-21 (Opus session) — audit batch committed + hardening batch

Never trust a SHA in this file — derive with `git rev-parse --short HEAD` / `git log --oneline -8`.
Roles switched this session: Claude Code (Fable for audit, Opus for building) is now primary coding
agent; Codex is the external auditor from Friday 2026-07-24. See
`Desktop/Career Seeker/Opus-Build-Roadmap-2026-07-21.md` for milestones (M-A..M-E) and gates (G1..G6).

Branch/PR topology now (all draft, none merged — awaiting Friday audit + Brandon):
- `main` @ `3fa65f5` — stale (156 behind the live line).
- `agent/repo-cleanup` @ `81d232c` — pre-audit live line; PR #1 → `main`, draft/open.
- `agent/audit-cleanup-h1h2h3` @ `f3021ec` — the previously-uncommitted H1/H2/H3 + CLAUDE.md, now
  committed. **PR #2 → `agent/repo-cleanup`** (draft, CI green).
- `claude/hardening-batch` — Phase-3 hardening, **PR #3 → `agent/audit-cleanup-h1h2h3`** (draft, CI green).
- `claude/alpha-finish` — Phase-4 alpha release candidate, based on the `claude/hardening-batch` tip so it
  carries every post-checkpoint commit. **PR #4 → `agent/repo-cleanup`** (draft). This PR is the
  consolidated "what Claude changed after the Codex checkpoint (`81d232c`)" diff you asked for; PR #2 and
  PR #3 remain open as the granular per-batch views of the same commits. Review whichever is more useful.

  Branch-base note: you suggested branching `claude/alpha-finish` directly off `agent/repo-cleanup`. Doing
  that literally would have dropped H1/H2/H3 and A1/L1/M1/M2, none of which are merged into
  `agent/repo-cleanup` yet. Basing on the hardening tip and targeting `agent/repo-cleanup` gives the same
  single clean diff against the checkpoint while preserving that work and the small per-item commits.

This session's commits on `claude/hardening-batch` (newest first — derive head yourself):
- `ci: also run on claude/** branches` — CI trigger fix (claude/** branches had no CI).
- `M2` — document accepted query-string doc-token tradeoff (no behavior change).
- `M1` — pin `$ExpectedOfflineTotal` in Verify-Alpha.ps1 and assert it (closes silent-total-drift;
  confirmed CI already runs the SQLite harnesses on windows-latest).
- `L1` — presence-check SQLite migration (`PRAGMA table_info`, no more throw-and-swallow) + pre-existing-DB
  migration test in StoreParityHarness.
- `A1` — reject IPv6 unspecified `::` in `PrivateNetworkGuard.IsPubliclyRoutable` + SSRF-guard test.

Verification (this session, on the hardening-batch tree): `dotnet build -c Release --warnaserror` 0W/0E;
`scripts\Verify-Alpha.ps1` **325 passed, 0 failed** (Researcher 52→53, StoreParity 19→22; pinned-total
assertion passes). Counts synced across docs + verifier per the CLAUDE.md drift trap.

Suggested Codex audit focus: A1 (`::` the only v6 gap?), L1 (no-FK old-schema seed acceptable? PRAGMA
column-index read), M1 (`$ExpectedOfflineTotal` now in the drift-trap set), M2 (documented acceptance, not
a fix — cookie migration is a deliberate follow-up if wanted). Remaining open from the 2026-07-20 audit
after this batch: none of A1/L1/M1/M2; M3/L2/L3 are documented-accepted residuals.

### Phase 4 — alpha release candidate (`claude/alpha-finish`)

Commits on this branch beyond the hardening batch:
- `docs: add Claude alpha build instructions and future design ideas` — the two previously-untracked docs,
  committed with owner approval after a secret-pattern scan (clean).

Exact commands run this session and their results:

| Command | Result |
| --- | --- |
| `dotnet build CareerSeeker.sln -c Release --warnaserror` | **0 warnings, 0 errors** |
| `scripts\Verify-Alpha.ps1` | **325 passed, 0 failed** (pinned-total assertion passes) |
| `scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | **passed** — details below |

`-IncludePublish`: win-x64 self-contained single-file publish succeeded; published-executable demo smoke
ran a SQLite demo cycle with `errors: 0`.

`-IncludePackage`: trusted-tester ZIP built at `output\release\CareerSeeker-alpha-win-x64.zip`
(~31.0 MB), `manifest: ok`, **46 checksums verified**; packaged dashboard smoke `errors: 0`; packaged
helper smokes, audit export, and evidence export/import into an isolated restore workspace all passed.
The ZIP is reproducible from committed source — it is a build artifact and is **not** committed
(`output/`, `.appdata/`, `secrets/`, `tmp/` are gitignored; `git status` is clean after the run).

Per-harness offline breakdown at this head: Slice 28 · EngineHarness 89 · ResearcherHarness 53 ·
HookHarness 14 · StoreParityHarness 22 · GatewayGateHarness 34 · DispatcherNoSendHarness 35 ·
LifecycleHarness 44 · RendererHarness 6 = **325**.

**Intentionally skipped, and why:**
- `-IncludeLive` (live BYOK/Gmail/Gateway smoke) and `-IncludeResearch` (live Brave + BYOK research) —
  these spend real provider credits and touch Gmail. Held behind the standing human gate (G2); the owner
  did not authorize live/spending runs this session. Codex's own guidance was to prefer dry-run/live-safe
  helpers.
- No real Gmail draft was created. No `Run-CareerSeeker-Live` LIVE path was exercised.
- Consequence: live evidence in this file dated 2026-07-20 is the most recent live proof; it predates
  A1/L1/M1/M2. Nothing in this batch touches the Gmail send/draft path, BYOK wiring, or the packaging
  scripts, but the live path has not been re-proven on this head.

**Known risks / open items for the Friday audit:**
- Live/research verification is stale by design (see above). Re-running `-IncludeLive` and
  `-IncludeResearch` is the highest-signal next evidence if the owner authorizes spending.
- A1's fail-closed multi-address rule still rejects legitimately multi-homed hosts that publish any
  private address — intended, but it is a behavioral tradeoff worth a second opinion.
- L1's migration test seeds an old-schema table without the foreign key; it proves column migration and
  round-trip, not FK-constrained upgrade behavior.
- M2 is an accepted residual, not a fix; the doc-route token still travels in the query string.
- `main` remains 156 commits behind the live line; the whole chain is unmerged pending your audit.

## Session Status (2026-07-21 earlier — audit-findings work, now superseded above)

- Branch: `agent/repo-cleanup`
- PR: `https://github.com/ShivaClaw/careerseeker/pull/1`
- Current head: **do not trust a SHA embedded in this file — run `git rev-parse --short HEAD`.**
  At this update the branch head is `81d232c Add Codex resume handoff`. Any version of this handoff
  is committed *above* the SHA it can name, so an embedded value is always at least one commit stale
  by construction — treat it as a snapshot pointer, not ground truth. (A prior revision claimed
  `bd2bf8c`, which was already one commit behind the head that recorded it; that drift is what audit
  finding H3 flagged.)
- Worktree: **not clean.** H2 (engine startup reconcile sweep) and H3 (this handoff correction) are in
  progress in the working tree and not yet committed. Run `git status -sb` before trusting any evidence
  below; the pushed head still predates the H2 change.
- Worktree at original handoff creation (2026-07-20): clean
- PR merge state at handoff creation: `CLEAN`
- GitHub CI at handoff creation: both `Build and offline harnesses` checks passed
- User instruction (2026-07-20 session): stop working; resume only after explicit user request. That
  resume happened on 2026-07-21 to work the audit findings.

## What Was Finished

- Wired and verified the alpha BYOK/Gmail/PDF path:
  - BYOK Anthropic/Gemini provider import through local DPAPI vault.
  - Live Tailor and Gate provider smoke.
  - Bounded Gate checks for alpha runs.
  - Real ATS-clean PDF renderer and Gmail draft attachment path.
- Hardened L1 no-send and local-control surfaces:
  - Dispatcher remains draft-only; send/submit paths are absent or throw.
  - Gmail label capability remains split from draft creation.
  - Dashboard controls use loopback, token, Host/Origin/Referer, content-type, and body-size checks.
  - Dashboard read/document routes reject foreign Host headers.
  - Dashboard document links serve only configured artifact roots.
- Hardened alpha package export/import:
  - Export skips secret-looking paths and artifact symlinks/junctions.
  - Import rejects unsafe paths, secret-looking entries, duplicates, unsupported entries, ambiguous database entries, too many entries, and oversized uncompressed contents.
  - Package import verifies restored SQLite audit chain.
- Hardened live research:
  - Brave adapter fetches public result pages before grounding.
  - URL filtering rejects localhost, private IPv4, link-local metadata, private IPv6, and non-text results.
  - Dossier prompt quarantine remains covered.
- Finished public trust-site sync:
  - `https://careerseeker.app/privacy/` contains Google API Limited Use and no-training language.
  - `https://careerseeker.app/autonomy-contract/` is live.
  - Homepage links to the Autonomy Contract.
- Built and verified trusted-tester packaging:
  - Release ZIP includes executable, launchers, scripts, docs, manifest, audit snapshot, and checksums.
  - Extracted package self-check passes.
  - Packaged helper smokes cover readiness, dashboard task dry runs/status, company research preview, selected-job preview, live dry-run, audit export, evidence export/import, BYOK clear, and Gmail disconnect.
- Refreshed external audit materials:
  - `docs/External-Audit-Handoff.md`
  - `docs/CareerSeeker-Project-Summary.md`
  - `README.md`
  - `src/Engine/README.md`
  - Historical audit note in `docs/repo-audit-2026-07-13.md`
  - PR #1 body synced from `docs/External-Audit-Handoff.md`

## Verification Evidence

Most recent known-good local evidence on current pushed head:

- `scripts\Verify-Alpha.ps1`
  - `297 passed, 0 failed`
- `scripts\Verify-Alpha.ps1 -IncludePublish`
  - default verifier passed
  - win-x64 single-file publish passed
  - published executable demo smoke passed
- `scripts\Verify-Alpha.ps1 -IncludePackage`
  - default verifier passed
  - trusted-tester ZIP built at `output\release\CareerSeeker-alpha-win-x64.zip`
  - release manifest/checksums verified
  - dashboard smoke passed
  - packaged helper smokes passed
  - evidence export/import smoke passed
- `scripts\Check-AlphaLiveReadiness.ps1 -RequireGmail -RequireByok`
  - Gmail OAuth client parsed
  - Gmail token vault present
  - BYOK providers present: `anthropic`, `google`
  - Brave Search configured via `BRAVE_SEARCH_API`
- `scripts\Verify-Alpha.ps1 -IncludeResearch`
  - default verifier passed
  - live GitLab research retrieved 10 docs
  - 4 grounded fallback facts
  - 0 dropped ungrounded facts
  - domain verified and recruiter identifiable
  - best hook: `GitLab has a public jobs page.`
- `scripts\Verify-Alpha.ps1 -IncludeLive`
  - default verifier passed
  - BYOK provider import passed
  - live Anthropic/Gemini provider smoke passed
  - Gateway Tailor/Gate/accounting smoke passed
  - required Gmail/BYOK startup doctor passed
  - dashboard one-shot smoke passed
- GitHub PR #1 checks:
  - both `Build and offline harnesses` check runs passed
  - merge state `CLEAN`

## Latest Important Commits

Newest first. This is historical context, not a live head pointer — confirm the actual head with
`git log --oneline -5` / `git rev-parse --short HEAD`.

- `81d232c Add Codex resume handoff` (records this handoff; the branch head as of 2026-07-20)
- `bd2bf8c Keep alpha verification evidence current`
- `114d0cd Reject foreign dashboard hosts on read routes`
- `db4a0a2 Harden Brave result URL filtering`
- `0948cfd Bound alpha package import size`
- `8360fc7 Record live trust site deployment`
- `41515ee Reject ambiguous alpha package databases`
- `4fddee1 Skip symlinks in alpha package export`
- `dfbb3bf Restrict dashboard documents to artifact roots`

## Known Remaining Gaps

These are not hidden pass conditions for the L1 technical alpha, but they are still product-launch work:

- Windows service/tray shell, polished installer, and code signing.
- OAuth production verification/CASA.
- Android relay/dashboard.
- Product-grade PDF visual polish beyond ATS-clean text PDF.
- Gmail label tree, intentionally deferred to preserve compose-only L1 scope.
- Broader legal/privacy review before public launch.

## Resume Checklist

When the user explicitly resumes:

1. Check repo/PR state (never trust a SHA copied from this file — derive it):
   - `git rev-parse --short HEAD` and `git log --oneline -5`
   - `git status -sb`
   - `gh pr view 1 --repo ShivaClaw/careerseeker --json mergeStateStatus,statusCheckRollup,headRefName,url`
2. If code changed externally, rerun the default verifier first:
   - `scripts\Verify-Alpha.ps1`
3. If preparing for another audit pass, prefer high-signal evidence:
   - `scripts\Verify-Alpha.ps1 -IncludePackage`
   - `scripts\Verify-Alpha.ps1 -IncludeLive`
   - `scripts\Verify-Alpha.ps1 -IncludeResearch`
4. Do not print secret values from `secrets/env.secrets`, OAuth client JSON, token vaults, or DPAPI vaults.
5. If any docs/evidence counts change, update the verifier doc-smoke expectations in `scripts/Verify-Alpha.ps1`.
6. If changes are made, run the relevant verifier, commit, push `agent/repo-cleanup`, and watch PR checks.

## Stop Marker

The user asked to stop working for this session. Do not continue autonomously from this file alone.
