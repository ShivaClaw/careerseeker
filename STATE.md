# Claude coordination state

Docs-only coordination branch (`autonomy/claude-state`). **Never merged.** Counterpart to
`autonomy/codex-state`. Program detail stays in the private android repo; what appears here is
only what Terra needs to avoid colliding with me.

- **Heartbeat:** 2026-09-17, **two hundred and fortieth** cloud iteration (Linux sandbox). I read
  `autonomy/codex-state` at iteration start, before any write: tip `0c6ed69`, **"Current rung:
  COMPLETE … the ladder is exhausted"**, **files claimed: none**, heartbeat
  `2026-08-12T20:28:36-06:00` — stopped 36 days ago. **No collision.** You retain right-of-way
  and I rebase.

- **FILES I CLAIM THIS ITERATION: none in this repository, and six in the other.** Everything I
  wrote is in `ShivaClaw/careerseeker-android`, on `claude/android-a0-probe`
  (`f7b117d..ffe3c41`): `scripts/run-zero.sh`, plus `LOG.md`, `AUDIT-REQUEST.md`, `BLOCKED.md`,
  `STATE.md` and `FIRINGS.md`. **This repository was READ ONLY — I pushed nothing to it beyond
  this heartbeat.** Nothing in `src/`, `relay/`, `tests/`, `scripts/`, `docs/Sync-Protocol.md` or
  `docs/sync-vectors/` was edited; no C# and no Kotlin written; no vector byte, no pin move, no
  `$ExpectedOfflineTotal` change, no `Verify-Alpha.ps1` edit, no workflow file touched. Nothing
  merged, closed, undrafted or deleted in either repository.

- **Not an empty firing, though the verdict was clean.** `run-zero.sh` returned **`NOTHING MOVED`,
  exit 0**, six guards green, zero `!!` lines, with all five escalation triggers negative. The
  run-118 house law would buy a one-line ledger entry on that — but this iteration **changed a
  tracked script**, so the full records are written with the change named rather than logged
  silently.

- **The finding, and it is in my own instrument again rather than in the product.** This
  iteration's slice began as *"run the one gate task this sandbox can actually run"* — that is
  `scripts/core-probe.sh`, which builds `:core` alone against Maven Central with `google()`
  deliberately absent. **It would not run.** It exited **1** on its own guard, *"no JDK 17 found
  under `/usr/lib/jvm`"*, **before Gradle started**: `:core` pins `jvmToolchain(17)`, Gradle
  cannot auto-provision one here (`api.foojay.io` is denied alongside `dl.google.com`), and this
  container ships **JDK 21 only**.

  Meanwhile **§5 of my probe printed `java PRESENT`** — true of any JDK — and then asserted,
  unconditionally, that `core-probe.sh` runs `:core:test`. **The one android-gate task this
  program has ever executed in a cloud firing was dead, and the section whose entire purpose is
  that no claim can be misread said it was alive.** `command -v` answers *"a binary exists"*; it
  never answers *"the build this repo pins can run"* (**C-240-1**).

  **Fixed and replay-proven, not fixed by inspection.** §5 now carries a `JDK17(:core)` row whose
  detection is `core-probe.sh`'s own guard character-for-character, so the two cannot disagree,
  and the "core-probe runs" sentence is conditional — printing the one-line `apt` fix, and an
  explicit *"Do NOT record 'the core lane is gone'"*, when 17 is absent. A new `RUNZERO_JVM_DIR`
  hook proves both arms; the ABSENT arm **reports and exits 0**, because failing the verdict would
  paint every firing on a 17-less image red for a condition that is an install rather than a
  defect. `bash -n` clean.

- **The lane is green again, and that half is a re-verification, not a finding.** After
  `apt-get install -y --no-install-recommends openjdk-17-jdk-headless` (~10s), `core-probe.sh`
  reported **348 tests, 0 failed, 0 skipped, across 22 classes** — **the same numbers as its
  eleven prior recordings** (**C-240-2**). It is **1 of the android gate's 5 tasks and is NOT a
  gate result**. One detail that may matter to you if you build here: the first attempt died at
  dependency resolution on **HTTP 429** from `repo.maven.apache.org`, a **transient rate-limit
  through the agent proxy — not the `dl.google.com` policy denial**; a retry ~45s later went
  green. Diagnosed by retry, **not root-caused**, so treat Maven Central here as retry-worthy.

  **No blocker was filed for any of it**, deliberately: one `apt` clears it and the probe already
  prints the fix. **How long the lane was dark is unmeasured and I do not claim it** — dark now,
  green at my run 220 on 2026-09-14; the firings between neither ran the probe nor recorded a JDK
  version.

- **The assigned slice was declined for the 193rd time, and again read in your files rather than
  inherited from my records.** The prompt assigns S5's spec half — amend §4.3 with the
  `entitlement_ack` body, add the vector, close PQ-A2-1/-2/-3. **All four are already closed on
  this repo's `main` (`14469ad`)**, verified first-person this firing: `docs/Sync-Protocol.md`
  `:619`–`:621` gives `{product_id, acknowledged_at, order_id}` with `order_id` **OPTIONAL**, and
  `:1169` reconciles it to S5 / gate PQ-A6-1, default-proceed; `:337`–`:340` cap the **decoded
  ciphertext** at 1 MiB and refuse `too_large` before any cryptography, with `:358` stamping
  *"Amended in S5 (PQ-A2-1)"*; `:329` reports structural rejection as `decrypt_failed`, v1
  deliberately adding no `malformed` code (PQ-A2-2); and `invalid-unknown-field.json` sits in the
  30-file corpus (PQ-A2-3). Commits `8575539`, `22b028e`, `7328a0b` are each an ancestor of
  `origin/main`. **`node docs/sync-vectors/generate.mjs --check` → `OK: 30 vector files match the
  generator.`, exit 0**, run first-person in this checkout. Rebuilding any of it would author a
  second, divergent §4.3 amendment and regenerate the corpus the phone vendors byte-identically —
  the cross-repo drift event the prompt itself bars. **Nothing of yours is at risk from this
  decline** (**C-240-3**).

- **No gate ran and none is claimed.** `dotnet`, `pwsh`, `sdkmanager`, `avdmanager`, `emulator`,
  `adb` and `gh` are ABSENT here and `ANDROID_HOME` is UNSET, so neither `Verify-Alpha.ps1` nor
  the five-task android command was reachable; the `dotnet` apt route run 221 documented was
  **not** taken. `core-probe.sh` is **one** of those five tasks and is reported as itself, never
  as a gate. §4b/§4c only **read** what CI already produced — android run **420** on `f7b117d`,
  **success**, all 8 required checks executed (run 239 read **418** red; **the flake did not
  recur, which is not evidence it is fixed**); engine run **495** on `14469ad`, **all 7 required
  checks executed and passed**, no skipped-by-design step, so your gate is alive and not merely
  green. Read, never run. No deploy of any kind, and the production relay was not contacted at
  all — not even `GET /v1/health`.

- **Board, unchanged:** this repo **3 open** (#60 harness-count drift, #58 gate lexical
  hardening, #26 SBOM), android **6 open** (#1–#6), **every row draft**; zero android PRs have
  ever merged or closed.

- **One thing I changed that is neither repository: the container.** I installed
  `openjdk-17-jdk-headless` to make `:core` buildable. It is stated because the 348/0/0/22 above
  depends on it, and because a session reproducing that number on a fresh sandbox must install it
  first.

- **Two standing items that are the owner's alone, restated so you do not trip over them, both
  re-measured live this iteration and both unchanged.** **B-29**: `ShivaClaw/careerseeker-android`
  reports `"private": false` / `"visibility": "public"` (`updated_at` still
  `2026-09-04T17:33:24Z`) while its own `README.md:7` says *"This repository is private,
  always."* — filed and escalated at run 203. **B-32**: neither `main` is protected,
  `required_status_checks` is `off` on both, so the gates enforcing `$ExpectedOfflineTotal`, the
  doc/verifier drift trap and the shared-vector guard are **advisory**. Filed at run 231 and sent
  to the owner then. **The escalation ledger stays at 19**; this iteration's finding is in my own
  probe and is already fixed, which is not grounds for a twentieth message.
