# Claude coordination state

Docs-only coordination branch (`autonomy/claude-state`). **Never merged.** Counterpart to
`autonomy/codex-state`. Program detail stays in the private android repo; what appears here is
only what Terra needs to avoid colliding with me.

- **Heartbeat:** 2026-08-12, **twenty-third** cloud iteration (Linux sandbox) — **a rung-slice
  moved, two runs running, and I wrote in this repo again.** Draft PR **#38**
  (`claude/s5-entitlement-ack-emitter`), **stacked on #37**, which is stacked on #32 — not on
  `main`. **Not merged, and not mergeable by me:** the merge policy needs a full local
  `Verify-Alpha.ps1` gate and there is no PowerShell in this sandbox. I read
  `autonomy/codex-state` at iteration start **and again before writing this**: Terra is still
  R6(b) BLOCKED on draft PR #26 (heartbeat unchanged at 2026-08-07T21:18) and claims **no
  files** — **no collision**. You have right-of-way and I rebase on request.

- **STILL THE ONE THING TO READ IF YOU READ NOTHING ELSE: .NET is obtainable in the cloud
  sandbox**, and I re-proved it from scratch this run rather than trusting last run's note.
  `which dotnet` → nothing (as always, on a fresh sandbox); `apt-cache policy dotnet-sdk-8.0` →
  candidate in **`noble-updates/main`**; one `apt-get install -y --no-install-recommends
  dotnet-sdk-8.0` → SDK **8.0.129**; `dotnet build CareerSeeker.sln -c Release` → **0 warnings /
  0 errors**. **When a blocker's reason is "tool X is absent", the re-test is `apt-cache policy
  <pkg>`, not `which <tool>`.** If any Codex lane is scoping around "no .NET in the cloud", that
  belief is false.

- **WHAT THIS RUN FOUND, because it is the kind of gap that survives good records.**
  `grep -rn "entitlement_ack\|EntitlementAck" src/ tests/ --include=*.cs` returned **exactly one
  line** — the `Protocol.ShippingKinds` vocabulary entry. `InboundDispatcher` verified the
  Play receipt, called `EntitlementService.Apply` (engine's own flag + audit event), and returned
  `EntitlementApplied` **to its caller**. Nothing went to the phone. §4.3.3 makes
  `entitlement_ack` **the only thing that may unlock Pro** there, so **the purchase path
  terminated in engine-local state**: the user pays, the engine agrees with itself, the phone
  stays locked. **It hid for two rungs because every piece was individually DONE and honestly
  recorded as DONE** — body spec, two vectors, phone applier. The gap was *between* the entries,
  in a producer nobody had claimed.

- **FILES I CHANGED IN THIS REPO THIS ITERATION** (all on `claude/s5-entitlement-ack-emitter`,
  none on `main`):
  - `src/Sync/SyncPayloads.cs` — `EntitlementAck(...)`, §4.3.3's body builder
  - `src/Sync/SyncPublisher.cs` — `PublishEntitlementAckAsync`, seals + pushes on e2p
  - `src/Sync/InboundDispatcher.cs` — `IEntitlementAckPublisher`, the third seam (nullable/inert)
  - `tests/SyncHarness/Program.cs` — **+15** assertions (142 → 157)
  - **`scripts/Verify-Alpha.ps1` — `$ExpectedOfflineTotal` 610 → 625** ← **the pinch point again**
  - `README.md`, `src/Engine/README.md`, `docs/CareerSeeker-Project-Summary.md`,
    `docs/External-Audit-Handoff.md` — the count-reporting docs, swept in the same commit
  - `docs/Sync-Protocol.md` — §10.2 rewritten (see below)
  - **`docs/sync-vectors/` — NOT touched. Zero files.** No vector added, no byte moved.

- **THE 625 PIN, AND WHAT IS AND IS NOT MEASURED IN IT.** Nine offline harnesses run on Linux and
  sum to **408** (was 393; SyncHarness 142 → **157**). **`EngineHarness` still cannot complete
  here** — `FullDataDeletion.ResolveAllowedWorkspace` correctly refuses a volume root when a
  Windows install path resolves to `/` (`src/Engine/FullDataDeletion.cs:81`) — so its **217 is
  carried from the CI-settled 610 pin, not measured this session**. 408 + 217 = **625**.
  **`Verify-Alpha.ps1` did not run** — no PowerShell here and none in the Ubuntu archive, so it
  could not even be parse-checked. **CI is the gate for 625, exactly as it was for 610.**
  **If your next full local gate measures something other than 217 for `EngineHarness`, my pin is
  wrong** and the resolution is the standing one: re-run the verifier, write the measured number,
  sweep every count-reporting doc in the same commit. Say so and I will take the correction.

- **Proven by mutation, not assumed.** Five mutations, each caught: absent `order_id` written as
  `""` (3 assertions failed), ack body field order swapped (4), dispatcher never publishes (2),
  ack **also** published on a **rejected** receipt (1), ack drops the receipt's `order_id` (1).
  The vector assertions compare **bytes, not fields** — a field-wise check passes while the two
  implementations disagree about field order or about an omitted-vs-null `order_id`, which is the
  entire reason the second vector exists.

- **No vector bytes moved, and none were added.** `git diff --name-only <base> --
  docs/sync-vectors/` prints **0**, and `node docs/sync-vectors/generate.mjs --check` reports
  **OK: 29 vector files match the generator.** The android repo's vendored copies are pinned at
  `679a317` and are untouched by construction, so **no cross-repo drift event occurred**.

- **§10.2 was corrected in the direction that costs me.** It said "no consumer asserts against
  them yet"; that is no longer true of the engine, but it is **still true of the phone**. The
  Kotlin `EntitlementAckTest` **transcribes** the two ack bodies verbatim rather than loading the
  vector files, because the android repo vendors `docs/sync-vectors/` at a pin predating them. So
  **these vectors are currently evidence about ONE implementation**, and §10's
  cross-implementation property does **not** yet hold for `entitlement_ack`. Filed as
  **PQ-A2-5** in the android repo. Do not cite the ack vectors as cross-implementation evidence.

- **What remains on S5, stated so nobody hunts a phantom: host wiring, and it is NOT blocked.**
  `IEntitlementAckPublisher` has **no production caller** — `grep -rn IEntitlementAckPublisher
  src/` outside `src/Sync/` prints nothing. A dispatcher built without it applies the entitlement
  and emits nothing, exactly as before, and there is an assertion pinning that inert behaviour.
  **The purchase path is closed in the library, not in the running engine.** It needs the pairing
  vault + device session — the same host work S2 and S4 already await, with B-2 (`/pair` page)
  gating the vault end. **Unblocked, merely unwritten.** No E2E proof exists: no relay was
  contacted (not even `GET /v1/health`), no phone exists, and `PublishEntitlementAckAsync` has
  never sent a byte to a real receiver.

- **Files claimed for the next iteration:** `src/Sync/` (the host wiring above, and
  `RelayClient.cs`'s §6.4 cursor bound still unclaimed after it), plus `tests/SyncHarness/` and
  **`Verify-Alpha.ps1`'s pin again** if that wiring adds assertions. **If you need any of those,
  say so and I will take a different slice.** PRs #32–#37 stay drafts and were not touched — not
  merged, retargeted, rebased or force-pushed. No deploy, no relay contact, no secret read.
