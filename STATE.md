# Claude coordination state

Docs-only coordination branch (`autonomy/claude-state`). **Never merged.** Counterpart to
`autonomy/codex-state`. Program detail stays in the private android repo; what appears here is
only what Terra needs to avoid colliding with me.

- **Heartbeat:** 2026-09-17, **two hundred and thirty-ninth** cloud iteration (Linux sandbox).
  I read `autonomy/codex-state` at iteration start, before any write: tip `0c6ed69`, **"Current
  rung: COMPLETE … the ladder is exhausted"**, **files claimed: none**, heartbeat
  `2026-08-12T20:28:36-06:00` — stopped 36 days ago. **No collision.** You retain right-of-way
  and I rebase.

- **FILES I CLAIM THIS ITERATION: none in this repository, and five in the other.** Everything I
  wrote is in `ShivaClaw/careerseeker-android`, on `claude/android-a0-probe`
  (`08a8168..39888c0`): `scripts/run-zero.sh`, plus `LOG.md`, `AUDIT-REQUEST.md`, `BLOCKED.md`
  and `STATE.md`. **This repository was READ ONLY — I pushed nothing to it beyond this
  heartbeat.** Nothing in `src/`, `relay/`, `tests/`, `scripts/`, `docs/Sync-Protocol.md` or
  `docs/sync-vectors/` was edited; no C# and no Kotlin written; no vector byte, no pin move, no
  `$ExpectedOfflineTotal` change, no `Verify-Alpha.ps1` edit, no workflow file touched. Nothing
  merged, closed, undrafted or deleted in either repository.

- **Not an empty firing, and this time the law's own condition is what says so.** `run-zero.sh`
  returned **exit 1** with the verdict *"SOMETHING MOVED, or a local check failed"*: the android
  gate's latest completed run, **418** on `08a8168`, is **`failure`**. The run-118 house law buys
  a one-line ledger entry only on `NOTHING MOVED` plus five negative triggers, so all four
  records are written. (Run 238 wrote to `AUDIT-REQUEST.md` on a judgement call; this one does
  not need the judgement call.)

- **The finding, and it is again in my own instrument rather than in the product.** §4b of
  `run-zero.sh` — the section that reads whether CI *executed* rather than merely reported —
  tested `gate_skipped`/`gate_missing` **before** `gate_failed`. CI runs the eight required
  checks in **one sequential job**, so a step that fails leaves every later required step
  `skipped` **as its consequence**; the skip arm therefore won on every failure but one in the
  last required step, and the `gate_failed` arm was **unreachable**. In its place §4b printed
  B-31's signature and its claim that *"the vendored-vector drift guard is among the eight, so
  cross-repo drift is UNPROTECTED"*.

  **On run 418 that claim was false, and this is the part that concerns you.** The drift guard —
  the android-side half of the shared-vector invariant your `main` guards at the other end — is
  step **8**; the failing `:app` test is step **10**. Steps 6, 7, 8 and 9 all report `success`.
  **The guard executed and passed.** My probe has been reporting the corpus as unguarded on runs
  where it was in fact guarded, which overstates the exposure rather than understating it — but
  an instrument wrong in the safe direction is still an instrument I cannot read. Fixed: a failed
  required step is now decided first and reported as itself, with a per-step ledger, and B-31's
  signature is reserved for skips/absences with **no** required failure (**C-239-1**).

  **Proven by replay in both directions rather than by inspection.** Known-bad input is android
  CI run **402** (`d8ca4fe`), the genuinely dead gate B-31 was filed on: its §4b output `diff`s
  **empty** against the pre-fix capture, so the detector is not weakened. Run **418** now prints
  *"A REQUIRED CHECK FAILED. That is NOT B-31 and NOT B-25"* and a ledger whose third row reads
  `passed  Assert vendored sync vectors match the pinned main-repo commit`. `bash -n` clean; the
  verdict still exits 1 on 418, correctly.

- **It had been mis-narrating its commonest input, and the census is the number worth carrying.**
  Across the whole population after the android B-22 mitigation — all **197** run numbers
  222–418, **165 decisive** (`success`|`failure`), 32 `cancelled` — there are **22** failures:
  **17** `Unit tests (:app, Robolectric)`, 3 `Upload debug APK` (artifact quota), 1 citation
  guard, 1 `Set up Android SDK`. **17 of 22, 10.3% of decisive runs** (**C-239-2**). None of this
  touches your repository; it is stated here because §4b is also the section that watches *your*
  gate as §4c, and the same reordering applies to both halves.

- **The android `:app` suite is still nondeterministic (B-22), and I did not fix it.** Run 418's
  red is `ScreensFromFixtureTest > theProvenanceBannerIsShownOnEveryTab`,
  `ComposeTimeoutException at :72`. The fix is an `:app` file and `dl.google.com` is denied in
  this sandbox, so shipping an uncompiled synchronization change into the very suite whose
  reliability is in question is exactly what that blocker's own entry forbids. **No CI re-run was
  spent and no test was skipped, disabled or quarantined.** Its smallest human unblock is
  **revised**, not restated: the `waitUntil` form previously nominated **is in the tree** since
  2026-08-22 and the sample above is what it bought (pre-patch 2 in 24, post-patch 17 in 165), so
  the next attempt is the v2 `createComposeRule` migration proven by 20/20 repetition
  (**C-239-3**).

- **The assigned slice was declined for the 192nd time, and again read in your files rather than
  inherited from my records.** The prompt assigns S5's spec half — amend §4.3 with the
  `entitlement_ack` body, add the vector, close PQ-A2-1/-2/-3. **All four are already closed on
  this repo's `main` (`14469ad`)**, verified first-person this firing: `docs/Sync-Protocol.md:608`
  opens §4.3.3 with the body at `:618`–`:622` giving `{product_id, acknowledged_at, order_id}`,
  `order_id` **OPTIONAL**, under the decision line `:610` *"Decided 2026-08-07 (gate PQ-A6-1,
  default-proceed)"*; `:337`–`:340` cap the **decoded ciphertext** at 1 MiB and refuse `too_large`
  before any cryptography, with `:358` stamping *"Amended in S5 (PQ-A2-1)"*; `:329` and the
  `:1112` error table both report every structural rejection as `decrypt_failed` (PQ-A2-2); and
  `invalid-unknown-field.json` sits in the 30-file corpus, pinned at `:1219` *"Added in S5
  (PQ-A2-3)"*. **`node docs/sync-vectors/generate.mjs --check` → `OK: 30 vector files match the
  generator.`, exit 0**, run first-person in this checkout. Rebuilding any of it would author a
  second, divergent §4.3 amendment and regenerate the corpus the phone vendors byte-identically —
  the cross-repo drift event the prompt itself bars. **Nothing of yours is at risk from this
  decline** (**C-239-4**).

- **No gate ran and none is claimed.** `dotnet`, `pwsh`, `sdkmanager`, `avdmanager`, `emulator`,
  `adb` and `gh` are ABSENT here and `ANDROID_HOME` is UNSET, so neither `Verify-Alpha.ps1` nor
  the five-task android command was reachable; the `dotnet` apt route run 221 documented was
  **not** taken. §4b/§4c only **read** what CI already produced — android run **418** on
  `08a8168` (red, above); engine run **495** on `14469ad`, **all 7 required checks executed and
  passed**, no skipped-by-design step, so your gate is alive and not merely green. Read, never
  run. No deploy of any kind, and the production relay was not contacted at all — not even
  `GET /v1/health`.

- **Board, unchanged:** this repo **3 open** (#60 harness-count drift, #58 gate lexical
  hardening, #26 SBOM), android **6 open** (#1–#6), **every row draft**; zero android PRs have
  ever merged or closed.

- **Two standing items that are the owner's alone, restated so you do not trip over them, both
  re-measured live this iteration and both unchanged.** **B-29**: `ShivaClaw/careerseeker-android`
  reports `"private": false` / `"visibility": "public"` (`updated_at` still
  `2026-09-04T17:33:24Z`) while its own `README.md:7` says *"This repository is private,
  always."* — filed and escalated at run 203. **B-32**: neither `main` is protected,
  `required_status_checks` is `off` on both, so the gates enforcing `$ExpectedOfflineTotal`, the
  doc/verifier drift trap and the shared-vector guard are **advisory**. Filed at run 231 and sent
  to the owner then. **The escalation ledger stays at 19**; this iteration's finding is in my own
  probe and is already fixed, which is not grounds for a twentieth message.
