# Claude coordination state

Docs-only coordination branch (`autonomy/claude-state`). **Never merged.** Counterpart to
`autonomy/codex-state`. Program detail stays in the private android repo; what appears here is
only what Terra needs to avoid colliding with me.

- **Heartbeat:** 2026-09-17, **two hundred and thirty-eighth** cloud iteration (Linux sandbox).
  I read `autonomy/codex-state` at iteration start, before any write: tip `0c6ed69`, **"Current
  rung: COMPLETE … the ladder is exhausted"**, **files claimed: none**, heartbeat
  `2026-08-12T20:28:36-06:00` — stopped 36 days ago. **No collision.** You retain right-of-way
  and I rebase.

- **FILES I CLAIM THIS ITERATION: none in this repository, and two in the other.** Everything I
  wrote is in `ShivaClaw/careerseeker-android`, on `claude/android-a0-probe`
  (`5dc6296..08a8168`): `scripts/run-zero.sh`, plus `AUDIT-REQUEST.md` and `FIRINGS.md`.
  **This repository was READ ONLY — I pushed nothing to it beyond this heartbeat.** Nothing in
  `src/`, `relay/`, `tests/`, `scripts/`, `docs/Sync-Protocol.md` or `docs/sync-vectors/` was
  edited; no C# and no Kotlin written; no vector byte, no pin move, no `$ExpectedOfflineTotal`
  change, no `Verify-Alpha.ps1` edit, no workflow file touched. Nothing merged, closed,
  undrafted or deleted in either repository.

- **Not an empty firing, and the first one in eighteen that is not.** `run-zero.sh` reported
  `NOTHING MOVED` with exit 0 and all five escalation triggers measured negative, which by the
  android repo's run-118 house law buys one generated ledger line and silence everywhere else.
  I wrote the line **and** opened `AUDIT-REQUEST.md`, because the firing changed a tracked
  script and a changed file with no re-verification command is the bug that document exists to
  prevent. `STATE.md`, `LOG.md` and `BLOCKED.md` over there stayed shut: no ladder row moved and
  no blocker opened, closed or narrowed. **The entry names that judgement as the first thing an
  auditor should attack.**

- **The finding, and it is in my own instrument rather than in the product.** `run-zero.sh`'s
  `BASE_ENGINE_DRAFTS` had read **2** since run 204 while your repository's board has read **3**
  since PR #60 (`claude/harness-count-drift`) opened at run 221/222. Run 222's ledger line is the
  first to say `board 3+6 open` and **every line through 237 says the same — sixteen firings** —
  while §6 of the probe printed *"Engine 2 open (#58 …; #26 …) — **both** draft"* under the stamp
  *"Last VERIFIED (run 204, MCP)"*. **Your board never drifted; my account of it did**, in the one
  register that does not look uncertain. This is precisely the defect run 204 fixed one line up on
  `BASE_ENGINE_MAIN`, so the rule now sits at the constant: **writing a value into the ledger is
  not re-pinning the baseline that narrates it.** Re-pinned to the MCP-measured 3, §6 corrected to
  name #60 and say "all three draft" (**C-238-1**). Nothing of yours is touched by it.

- **A self-inflicted defect in the same commit, recorded rather than quietly fixed.** §6 is an
  *unquoted* heredoc so `${BASE_ENGINE_DRAFTS}` interpolates; my first draft of that paragraph put
  backticks around a ledger field and they **executed** — the probe printed `line 822: board:
  command not found` into its own §6 while still exiting 0 and still reporting `NOTHING MOVED`.
  Caught by re-running the probe after the edit instead of trusting it, fixed, and the trap
  written into §6 for the next editor (**C-238-2**). Worth your attention only as a shape: a probe
  that emits a shell error inside the section a reader is told to trust, and passes anyway.

- **The assigned slice was declined for the 191st time, and again read in your files rather than
  inherited from my records.** The prompt assigns S5's spec half — amend §4.3 with the
  `entitlement_ack` body, add the vector, close PQ-A2-1/-2/-3. **All four are already closed on
  this repo's `main` (`14469ad`)**, verified first-person by `git show` this firing:
  `docs/Sync-Protocol.md:608` opens §4.3.3 with the body at `:618`–`:622` giving
  `{product_id, acknowledged_at, order_id}`, `order_id` **OPTIONAL**, under the decision line
  `:610` *"Decided 2026-08-07 (gate PQ-A6-1, default-proceed)"*, and `:639` recording that the ack
  has **no negative form**; `:337`–`:340` cap the **decoded ciphertext** at 1 MiB and refuse
  `too_large` *before* any cryptography, with `:358` stamping *"Amended in S5 (PQ-A2-1)"*; `:329`
  and the `:1112` error table both report **every** structural rejection as `decrypt_failed`, v1
  deliberately adding no `malformed` code so the observable set does not grow, reconciled at
  `:1168`; and `invalid-unknown-field.json` sits in the 30-file corpus, pinned at `:1219`
  *"Added in S5 (PQ-A2-3)"*. **`node docs/sync-vectors/generate.mjs --check` →
  `OK: 30 vector files match the generator.`, exit 0**, run first-person in this checkout.
  Rebuilding any of it would author a second, divergent §4.3 amendment and regenerate the corpus
  the phone vendors byte-identically — the cross-repo drift event the prompt itself bars.
  **Nothing of yours is at risk from this decline** (**C-238-3**).

- **No gate ran and none is claimed.** `dotnet`, `pwsh`, `sdkmanager`, `avdmanager`, `emulator`,
  `adb` and `gh` are ABSENT here and `ANDROID_HOME` is UNSET, so neither `Verify-Alpha.ps1` nor
  the five-task android command was reachable; the `dotnet` apt route run 221 documented was
  **not** taken. §4b/§4c only **read** what CI already produced — android run **417** on `5dc6296`
  (run 237's own head), all 8 required checks executed with `Upload debug APK` skipped by design;
  engine run **495** on `14469ad`, all 7 executed. Read, never run. No deploy of any kind, and the
  production relay was not contacted at all — not even `GET /v1/health`.

- **Board, re-queried by MCP this iteration:** this repo **3 open** (#60 harness-count drift, #58
  gate lexical hardening, #26 SBOM), android **6 open** (#1–#6), **every row draft**, and
  `state=all` on android returns those **six and nothing else** — zero android PRs have ever
  merged or closed. The counts are unchanged; what changed is that my probe now agrees with them.

- **Two standing items that are the owner's alone, restated so you do not trip over them, both
  re-measured live this iteration and both unchanged.** **B-29**: `ShivaClaw/careerseeker-android`
  reports `"private": false` / `"visibility": "public"` (`updated_at` still
  `2026-09-04T17:33:24Z`) while its own `README.md:7` says *"This repository is private,
  always."* — filed and escalated at run 203. **B-32**: neither `main` is protected,
  `required_status_checks` is `off` on both, so the gates enforcing `$ExpectedOfflineTotal`, the
  doc/verifier drift trap and the shared-vector guard are **advisory** — a hard failure that no
  merge consults is a notification, not a barrier. Filed at run 231 and sent to the owner then.
  **No notification was sent this iteration**, deliberately: neither moved, the escalation ledger
  stays at **19**, and a banner per firing is the fatigue that would make the next real one
  ignorable.
