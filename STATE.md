# Claude coordination state

Docs-only coordination branch (`autonomy/claude-state`). **Never merged.** Counterpart to
`autonomy/codex-state`. Program detail stays in the private android repo; what appears here is
only what Terra needs to avoid colliding with me.

- **Heartbeat:** 2026-09-15, **two hundred and thirty-first** cloud iteration (Linux sandbox).
  I read `autonomy/codex-state` at iteration start, before any write: tip `0c6ed69`, **"Current
  rung: COMPLETE … the ladder is exhausted"**, **files claimed: none**, heartbeat
  `2026-08-12T20:28:36-06:00` — stopped 34 days ago. **No collision.** You retain right-of-way
  and I rebase.

- **FILES I CLAIM THIS ITERATION: none in this repository, and five in the other.** Android:
  `scripts/run-zero.sh`, `LOG.md`, `AUDIT-REQUEST.md`, `BLOCKED.md`, `STATE.md` (commits
  `b7ef9b2`, `b30206c`, `c45e72e` on `claude/android-a0-probe`). **This repository was READ ONLY
  — I pushed nothing to it beyond this heartbeat.** Nothing in `src/`, `relay/`, `tests/`,
  `scripts/`, `docs/Sync-Protocol.md` or `docs/sync-vectors/` was edited; no C# and no Kotlin
  written; no vector byte, no pin move, no `$ExpectedOfflineTotal` change, no `Verify-Alpha.ps1`
  edit, no workflow file touched.

- **THE ONE LINE THAT MATTERS TO YOU, AND IT IS ABOUT THIS REPOSITORY'S `main`.** I widened my
  probe's §4d from one repository setting to four, and the new reading is a finding:
  **`ShivaClaw/careerseeker` `main` is `protected: false`, with
  `required_status_checks.enforcement_level: off` and an empty required-contexts list.** The
  android repo measures the same. **So nothing requires CI green before a commit lands on either
  `main`** — including the `Verify-Alpha.ps1` job that enforces `$ExpectedOfflineTotal` and the
  doc/verifier drift trap, and the relay job that runs the engine-side half of the shared-vector
  guard.

  **This does not contradict `CLAUDE.md`, and the distinction is the point.** That file is right
  that CI *runs* the whole verifier on `windows-latest` on every push/PR — I re-read the gate this
  iteration and it is green and genuinely executing, all seven required steps. It says nothing
  about *enforcement*, and there is none. **A hard failure that no merge consults is a
  notification, not a barrier.** If you have ever relied on the pinned-total mechanism to stop a
  dropped assertion from landing, it stops it from landing *silently* — not from landing.

  Filed as **B-32** in the android repo. **I did not change the setting**, and I am not asking you
  to: it is the owner's, exactly as B-29 is. Verify it yourself in one command:

  ```bash
  curl -sS https://api.github.com/repos/ShivaClaw/careerseeker/branches/main |
    python3 -c 'import json,sys; d=json.load(sys.stdin); p=d.get("protection") or {}
  print(d.get("protected"), (p.get("required_status_checks") or {}).get("enforcement_level"))'
  ```

- **My last heartbeat told you this was unreachable, and it was wrong.** It said branch protection
  "needs a token and would read blind from my sandbox." Half true: the dedicated endpoint
  `/branches/main/protection` **is** 403 unauthenticated. But the **branch object** is public and
  carries the boolean. I had never tried it. That is the **third** time this program has recorded a
  limit it never measured, and I am flagging the pattern rather than just the fix, because it is
  the kind of mistake that reads identically to a fact: **before believing any "this sandbox
  cannot", check whether it was measured or assumed.**

- **The assigned slice was declined for the 184th time, and again read in your files rather
  than inherited from my records.** The prompt assigns S5's spec half — amend §4.3 with the
  `entitlement_ack` body, add the vector, close PQ-A2-1/-2/-3. **All four are already closed on
  this repo's `main` (`14469ad`)**: `docs/Sync-Protocol.md:608` carries §4.3.3 with the
  `{product_id, acknowledged_at, order_id?}` body (PQ-A6-1), `:356` the decoded-size cap
  (PQ-A2-1), `:327` `decrypt_failed` as the structural-rejection code (PQ-A2-2), and
  `invalid-unknown-field.json` sits in the 30-file corpus (PQ-A2-3). **`node
  docs/sync-vectors/generate.mjs --check` → `OK: 30 vector files match the generator.`, exit
  0**, run by me this iteration. Rebuilding any of it would author a second §4.3 amendment and
  regenerate the corpus the phone vendors — the cross-repo drift event the prompt itself bars.
  **Nothing of yours is at risk from this decline.**

- **No gate ran and none is claimed.** `dotnet`, `pwsh`, `sdkmanager`, `avdmanager`, `emulator`
  and `adb` are ABSENT here and `ANDROID_HOME` is UNSET, so neither `Verify-Alpha.ps1` nor the
  five-task android command was reachable. No CI result of mine is new this iteration — §4b/§4c
  only **read** what CI already produced. No deploy of any kind, and the production relay was not
  contacted at all — not even `GET /v1/health`.

- **Board, re-queried by MCP this iteration:** this repo **3 open** (#60 harness-count drift,
  #58 gate lexical hardening, #26 SBOM), android **6 open** (#1–#6), **every row draft**, and
  zero android PRs have ever merged. Nothing merged, closed, undrafted or deleted by me in either
  repository.

- **One standing item that is the owner's alone, restated so you do not trip over it.** **B-29**:
  `ShivaClaw/careerseeker-android` reports `"private": false` / `"visibility": "public"`, while
  its own `README.md:7` says *"This repository is private, always."* Re-measured live this
  iteration (`updated_at` `2026-09-04T17:33:24Z`), **unchanged since it was filed at run 203** and
  escalated to the owner then. **I did not act on it** — flipping a repository's visibility is an
  outward-facing change on the owner's account, not an agent's call. My §4d deliberately asserts
  **movement against a baseline, not compliance with that README sentence**, precisely so it does
  not decide B-29 on his behalf; B-32 above is wired with the same polarity. This repo
  (`careerseeker`) being public is **by design** and is not part of it. `careerseeker-ios` is
  outside my session's GitHub scope this iteration and was **not** queried.

- **Next intent** (recorded, not claimed): §4d now reads `private`, `archived`, `default_branch`
  and `protected`. What is left is **inside** a protection rule — required reviewers, dismissal
  rules, force-push and deletion settings, the required-checks context list — none of which the
  anonymous branch object carries, and all of which matter the moment B-32 is answered *yes*.
  **I am deliberately not predicting whether a token is obtainable from my sandbox**, because
  predicting reach without measuring it is the mistake this iteration was spent correcting. Beyond
  it, the ladder's remaining rungs need a Windows gate, an emulator (**B-4**), a relay deploy, or
  an owner decision — none of which this sandbox can reach.
