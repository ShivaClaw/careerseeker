# CareerSeeker — Integration & Windows Phase Roadmap

**Status at this point.** The entire engine *brain* is built and verified offline: Verifier, Scout,
Store, Scorer, Pipeline, Tailor (with the dossier hook seam), LLM Gateway, Dispatcher, Researcher, and
the Engine host shell (cycle + scheduler + localhost dashboard). The vertical slice proves it composes —
DISCOVERED → DRAFTED with the Gate, the scam floor, and grounding all holding across module seams. Every
network/OS edge is behind a clearly-marked port with a compile-verified real client and a fake for tests.

What remains cannot run in the sandbox: it needs a Windows host, a Google Cloud project, OAuth, real
provider keys, headless Chromium, and code signing. This document sequences that work.

Two principles drive the ordering:
1. **Start the long-lead clocks on day one.** CASA security assessment, OAuth production verification, and
   public code signing are external, slow, and gate public launch. They run in parallel with all build
   work.
2. **Each port graduates the same way.** It already has an offline harness; "done" for the real client
   means a live smoke test that mirrors those same assertions against the real endpoint.

---

## Workstream A — Foundations & the long-lead clocks (start immediately, in parallel)

**A1. Windows dev environment.** Windows 11 box or VM. .NET 8 SDK, Playwright (`playwright install`),
Git. Pull the solution, restore packages from nuget.org (including `Microsoft.Data.Sqlite`), and run the
Store parity harness against SQLite. Confirm
the full solution builds and the harnesses (slice, engine, researcher, hook, store parity, gateway gate,
dispatcher no-send) run green on Windows. **Exit:** `dotnet test`/harnesses green on Windows.

**A2. Google Cloud project + OAuth consent screen.** Create the project; configure the OAuth consent
screen; register the desktop app client. Request **only `gmail.compose`** for v0.1. The L1 application
creates drafts only and has no send implementation, although this permission can authorize Gmail sends;
it grants no inbox or calendar access. Add yourself + beta users as test users (OAuth "testing" mode allows ≤100
users with no verification, which unblocks the entire closed beta before any CASA spend). **Exit:** an
OAuth client that can mint a `gmail.compose` token for a test user.

**2026 console note:** Google's old OAuth consent screen menu is now organized under Google Auth
Platform: Branding, Audience, and Data Access. Keep the scope list to `gmail.compose`; Google lists that
Gmail scope as restricted, so production use still needs verification and possibly assessment. Official
refs: [Google Auth Platform setup](https://developers.google.com/workspace/guides/configure-oauth-consent),
[restricted Gmail scopes](https://support.google.com/cloud/answer/13464325?hl=en).

**A3. Start the CASA / OAuth verification clock (paperwork only, now).** Restricted scopes (`gmail.compose`
is restricted) need OAuth app verification **and** a CASA Tier-2 security assessment by an authorized
third-party assessor before public launch — weeks-to-months and a recurring annual cost. File the
verification request and get assessor quotes now; the assessment itself can complete during beta.
**Flag:** launch-blocking, recurring cost. Keep scopes minimal to keep this cheap. **Exit:** verification
submitted, assessor engaged.

**A4. Code signing.** Prefer Azure Artifact Signing (formerly Trusted Signing) for non-Store Windows
distribution if eligibility fits; otherwise use an OV certificate from a traditional CA. Public signing
identity validation is slow, and new files still need SmartScreen reputation to build; EV is no longer
worth buying solely to bypass SmartScreen. Official refs: [Windows code-signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options),
[Artifact Signing](https://azure.microsoft.com/en-us/products/artifact-signing). **Flag:** launch-blocking
signing setup. **Exit:** signing account/certificate profile ready and the installer/exe signing command
documented.

**A5. Secrets vault.** Implement the DPAPI-scoped vault file for OAuth refresh tokens, provider API keys,
and E2E sync keys (spec §5.2). The current alpha uses per-user DPAPI vaults because OAuth and the dashboard
run in the user's Windows profile; a future Service + tray build must make the process owner explicit or add
a broker/migration path because service-scoped and user-scoped DPAPI material are not interchangeable. Nothing
sensitive belongs in the DB.
**Exit:** round-trip store/load of a secret under DPAPI, verified on Windows.

---

## Workstream B — Realize the connectors (each: real client + live smoke test)

Ordered easiest-first so momentum builds before OAuth is involved. Each step swaps a fake for the real
client already written and graduates it with a live test mirroring its offline harness.

**B1. Scout — real ATS ingestion (no auth).** Greenhouse / Lever / Ashby public JSON boards over HTTP.
No OAuth, so this is the cheapest real win. Wire the real HTTP path, run against 2–3 live boards, confirm
canonicalization + dedup against real data. **Exit:** real postings land in the Store via a live run.

**B2. LLM Gateway — real providers, BYOK first.** Plug Anthropic + Google keys into the
compile-verified provider clients. **BYOK needs no CASA** (user's own key), so validate real completions
and real cost accounting here first. Verify the pinned-Gate routing and budget throttle against real
token usage. Defer the Managed proxy (our metered key) until billing exists. **Exit:** a real tailoring +
a real Gate entailment call, priced correctly in the meter.

**B3. Researcher — real web research.** Implement `IWebResearch` against a search API (Brave/Bing/SerpAPI)
+ `web_fetch`. Confirm the grounding filter behaves on real pages (it will drop more than the fakes —
that's correct). **Exit:** a real dossier with grounded hooks for a real company.

**B4. Document renderer — headless Chromium.** Implement `IDocumentRenderer` (resume HTML template →
ATS-clean single-column PDF) via Playwright/Chromium. Content-address the output on disk; the Store row
holds the hash (spec §6). **Exit:** a real resume PDF rendered and attached to a draft package.

**B5. Gmail draft client — OAuth `gmail.compose`.** Wire the OAuth token (A2/A5) into the
compile-verified `GmailDraftClient`. Create real drafts in a test account; confirm the `CareerSeeker/*`
label tree and that **no application send path exists**. This is the L1 payoff — a real reviewable draft in Gmail.
**Exit:** a tailored application appears as a Gmail draft for a test user.

---

## Workstream C — The host & the composition root

**C1. Composition root.** One place that constructs every module with real adapters (or fakes via
config), reading secrets from the DPAPI vault and the profile/config from SQLite. This is the vertical
slice's wiring, productionized. **Exit:** a console "run one cycle for real" that drafts a real
application end-to-end.

**C2. Windows Service host.** Wrap `EngineHost` in a `Microsoft.Extensions.Hosting` `BackgroundService`;
install it under an explicit per-user identity/task model (auto-start on sign-in, graceful stop) unless a
separate broker/migration design is approved. SQLite lives under a per-user root such as
`%LOCALAPPDATA%\CareerSeeker\seeker.db` (WAL), not a machine-global `%ProgramData%` default. The localhost
dashboard (already built) serves status.
**Exit:** the service runs cycles unattended across a reboot; dashboard shows live counters.

**C3. Resilience.** Nightly encrypted SQLite backup (14-day rotation), export/import-workspace commands
for machine migration (spec §6), a smoke-test-on-startup that validates config/keys/connectivity before
the first cycle (spec §1.1 instinct). **Exit:** kill + restore the service with no data loss.

---

## Workstream D — Onboarding & the desktop UX

**D1. Onboarding interview.** Resume parse → pre-fill profile claims → "interview by exception" only for
gaps (spec §1.1). This populates the Gate's oracle (`SourceClaim`s in the Store), so its quality drives
the entire pipeline — invest here. Capture the Approved Answer Bank and the style/voice card too.
**Exit:** a finished onboarding writes a complete profile + claims to the Store.

**D2. WinUI 3 tray app.** System-tray presence: status, the kill switch (spec §4), open-dashboard, and
the autonomy-level toggle (locked to L1 for v0.1). **Exit:** tray controls drive the running service.

**D3. Installer + signing.** MSIX (or MSI) packaging the service + tray + onboarding; sign with the
A4 certificate. **Exit:** a signed installer brings up a working L1 install on a clean machine.

---

## Workstream E — Closed beta → public L1 launch

**E1. Closed beta (OAuth testing mode).** ≤100 test users, real Gmail drafts, real jobs, L1 Drafts only.
No CASA needed yet. Watch the KPIs: fabrication-gate escapes (target zero), scam-floor correctness, draft
quality, cost/user. **Exit:** a cohort runs for weeks with zero fabrication escapes and acceptable cost.

**E2. CASA assessment completes + OAuth production verification (A3).** Pass the security assessment;
move the OAuth app to production. **Flag:** the gate to public launch. **Exit:** verified app, unlimited
users for `gmail.compose`.

**E3. Public L1 launch.** Free Windows .exe, Drafts mode, localhost dashboard/evidence review. Email digest
and magic-link approvals remain L2/relay scope unless an explicit send-capable product path is designed and
verified. Managed-inference billing if the Managed tier ships (else BYOK-only at launch). **Exit:** v0.1 live.

---

## Workstream F — Paid Android companion (post-L1)

**F1. Sync relay.** A blind relay (E2E-encrypted; the server can't read content) pushing engine
state/counters to the phone. Keys in the DPAPI vault; pairing via QR.
**F2. Android app (Kotlin).** The dashboard remoted: jobs found/applied, score breakdowns, dossiers, the
documents sent, kill switch. This is the $1–5 paid product.
**F3. Play Store** listing + billing. **Exit:** paid dashboard live.

---

## Workstream G — L2 / L3 autonomy (post-revenue; the expensive scopes)

Deferred deliberately — these add the restricted scopes that drive CASA cost and risk.
**G1. `gmail.send`** + the Correspondent (reply handling) — L2 send becomes a one-tap gate, L3 automatic.
**G2. Inbox monitoring** (interview-request detection) — new restricted scope, new CASA scope.
**G3. Calendar** (free/busy within pre-authorized windows, event write) — the Schedulist.
**G4. Playwright auto-submit** for ATS forms — the highest-risk automation; behind L3 only, never on
read-only boards (LinkedIn/Indeed stay manual-finish forever).
**Flag:** each new restricted scope re-triggers CASA cost. Sequence by revenue.

---

## Immediate next session (after reset)

1. **A2 + B1 together:** stand up the Google Cloud project/OAuth client (paperwork for A3 in parallel),
   and — needing no auth — wire **Scout's real ATS ingestion** and run it against live Greenhouse/Lever/
   Ashby boards. That gives real data flowing into the Store on day one while the OAuth/CASA clocks tick.
2. Then **B2 (BYOK providers)** so the Gateway runs real inference with real cost accounting.

That sequence turns the verified brain into a thing that pulls real jobs and writes real (BYOK) drafts
without waiting on any long-lead external gate.

---

### Dependency & risk summary

- **Critical path to public L1:** A2 → B5 (real drafts) → C2 (service) → D1/D2/D3 (UX + installer) →
  E1 (beta) → A3/E2 (CASA + verification) → E3 (launch).
- **Long-lead / start now:** A3 (CASA + OAuth verification), A4 (code signing).
- **Recurring cost:** CASA assessment (annual), Managed-tier inference (if offered), provider usage.
- **No-auth quick wins (do first):** B1 (Scout), B2 (BYOK Gateway), B3 (research), B4 (PDF render).
- **Deferred by design:** Managed proxy billing, Android/relay (F), all L2/L3 scopes (G).
