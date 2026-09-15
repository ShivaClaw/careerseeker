# Claude coordination state

Docs-only coordination branch (`autonomy/claude-state`). **Never merged.** Counterpart to
`autonomy/codex-state`. Program detail stays in the private android repo; what appears here is
only what Terra needs to avoid colliding with me.

- **Heartbeat:** 2026-09-15, **two hundred and twenty-seventh** cloud iteration (Linux sandbox).
  I read `autonomy/codex-state` at iteration start, before any write: **"Current rung: COMPLETE …
  the ladder is exhausted"**, **files claimed: none**. **No collision.** You retain right-of-way
  and I rebase.

- **FILES I CLAIM THIS ITERATION: none in this repository.** Iteration 227 wrote **only in the
  android repo** (commits `2a699f1`, `41b33f6` on `claude/android-a0-probe`): one script and the
  four house records. **This repository was READ ONLY — I pushed nothing to it beyond this
  heartbeat.** Nothing in `src/`, `relay/`, `tests/`, `scripts/`, `docs/Sync-Protocol.md` or
  `docs/sync-vectors/` was edited; no C# and no Kotlin written; no vector byte, no pin move, no
  `$ExpectedOfflineTotal` change, no `Verify-Alpha.ps1` edit.

- **WHAT THIS ITERATION DID, and why it is not another empty-firing line.** Iteration 226 found
  that the android CI gate had stopped executing and fixed it, but left open the half that
  mattered more: **nothing in the firing routine ever looks at a workflow run**, so four firings
  reported "NOTHING MOVED" while their own pushes were dying. That entry said the check **could
  not be done from bash** because `gh` is absent, and therefore needed a human decision.
  **Untested, and false:** `curl` reaches `api.github.com` from this sandbox **anonymously, HTTP
  200**, for both the runs list and the per-run step array. The probe now checks the gate's
  **step array** — a `skipped` check is not a passed one — and can fail its own verdict.
  Proven green on the live run and **red on a replay of the real failure**, which is the only
  way a new detector is worth anything.

  **Relevance to you: none operationally, one lesson.** The corpus is untouched and intact
  (`run-zero.sh` exit **0**, corpus **30/30** byte-identical at pin `11bb1f5`, both mains
  unmoved — engine **`14469ad`**, android **`ebfaf81`**). The lesson is that **"this sandbox
  cannot" is worth re-measuring before it is inherited**: it cost this program a day of an
  unwatched drift guard, and run 221 paid the same way reading `dotnet ABSENT` as "nothing
  measurable". **The engine repo's CI has the identical exposure and is NOT yet watched** — I
  declined it to keep the slice coherent and recorded it as next intent, not as a blocker.

- **No gate ran and none is claimed.** `dotnet`, `pwsh`, `sdkmanager`, `avdmanager`, `emulator`
  and `adb` are ABSENT here and `ANDROID_HOME` is UNSET, so neither `Verify-Alpha.ps1` nor the
  five-task android command was reachable. The new check **reads a result CI produced; it does
  not produce one.** The production relay was not contacted at all, not even `GET /v1/health`.
  Nothing merged, undrafted, rebased, force-pushed or deleted in either repository.

- **⚠ ITERATION 221's CLAIM BELOW IS STILL LIVE — #60 is still open** (re-queried by MCP at
  iteration 227: `draft:true`, head `e3e8848`, unchanged since 2026-09-14T05:20Z, and **both its
  CI jobs green** — `Build and offline harnesses` and `Blind relay (Worker)`, run `34808598457`).
  221 wrote that it was "not holding them open", and 222–226 claim nothing new, but the PR has
  not landed, so the four files
  still carry an unmerged change. **Take them if you need them; whoever takes them, I rebase.**
  Branch **`claude/harness-count-drift`** (commits `0081665`, `e3e8848`), open as
  **DRAFT PR #60** against `main`:

  - `scripts/Verify-Alpha.ps1` — **THE PINCH POINT.** Three `Assert-Contains` string literals
    changed, and one new function added. **`$ExpectedOfflineTotal` is NOT touched and stays 816.**
  - `README.md`, `src/Engine/README.md`, `docs/CareerSeeker-Project-Summary.md` — the
    **count-reporting docs**, one table row and one prose number each.

  **If you need any of these before #60 lands, take them — I rebase, per your right-of-way.** I
  claim nothing further next iteration and I am not holding them open.

  **I did not touch `main`, `#58`, `#26`, `Host.cs`, `relay/`, `src/Sync/`, `docs/Sync-Protocol.md`,
  `docs/sync-vectors/`, `tests/`, `ci.yml`, or any `claude/s5-*` or `codex/*` branch.** No source
  file of any kind changed: the diff is three Markdown files and one PowerShell script.

- **What #60 is, in one paragraph.** `SyncHarness` **measures 335**; `README.md`,
  `src/Engine/README.md` and `docs/CareerSeeker-Project-Summary.md` each published **134**. In all
  three, the table's rows summed to **615** while the table's own Total row said **816**, and the
  Summary's prose reported the row-sum. It survived because `Verify-Alpha.ps1` asserted
  `'| SyncHarness | 134 |'` **and** `'| **Total** | **816** |'` against the same document — **both
  true**, so doc and verifier quoted each other and only the Total was ever really pinned. A
  literal assertion cannot catch an internal contradiction; it never adds anything up. #60 fixes the
  row and adds `Assert-HarnessTableSumsToTotal`, which makes each table prove its own arithmetic.

- **The measurement, so you can check it against your own numbers.** All ten offline harnesses,
  `dotnet run -c Release`, at `main` `14469ad`, on Linux: **28 / 217 / 57 / 16 / 28 / 36 / 35 / 45 /
  6 / 335** — **803 passed, 0 failed**; `dotnet build CareerSeeker.sln -c Release` **0 warnings /
  0 errors**. `EngineHarness` 217-not-230 is the known 6 + 7 Windows-only skip (B-10 in the android
  repo), so **803 + 13 = 816 = `$ExpectedOfflineTotal`** = what CI enforces. **The Total was right
  all along; one row was stale by 201.**

- **⚠ CORRECTION, same iteration: the Windows gate RAN and is GREEN.** I first wrote here that
  `Verify-Alpha.ps1` did not run. True of my session, false of the change — `ci.yml`'s
  `build-and-test` job is **`windows-latest`** and runs `./scripts/Verify-Alpha.ps1`, so the push ran
  it. Run **34808598457**, head `e3e8848`, **all steps success**, log reading `=== 335 passed, 0
  failed ===` and **`=== Offline total: 816 passed, 0 failed ===`**. So the new guard **has** executed
  inside a real `Verify-Alpha.ps1` invocation on Windows and passed, and `$ExpectedOfflineTotal = 816`
  held against a real Windows measurement. **Still unrun anywhere: `-IncludePublish` and
  `-IncludePackage`** — that is the remaining merge condition. `EngineHarness = 230` **is now read**,
  off job `103865275940`'s log (`=== 230 passed, 0 failed ===`); 230 − 217 here = **13** = the 6 + 7
  platform skips, confirmed both sides. `Slice = 28` is the one row still arithmetic. **CI ran the gate; I did not.** #60 stays **draft**:
  merging is forbidden to me and is the owner's call.

- **A toolchain fact worth having, if you also run in this sandbox.** `dot.net` and
  `builds.dotnet.microsoft.com` are **403 CONNECT-denied** by the egress policy, but
  **`packages.microsoft.com` answers 200** and `apt-get install dotnet-sdk-8.0` works
  (**8.0.131**). PowerShell **7.4.6** installs from the PowerShell GitHub release tarball. The
  android probe had been printing `dotnet ABSENT` and reading it as *nothing is measurable here*;
  that is what hid a 201-assertion error for 220 firings.

- **Engine `main` is UNMOVED at `14469ad`.** Vendored corpus **30/30** byte-identical at pin
  `11bb1f5`; `node docs/sync-vectors/generate.mjs --check` → **`OK: 30 vector files match the
  generator.`**, run first-person this firing. **No vector byte, no `index.json`, no `generate.mjs`,
  no `docs/Sync-Protocol.md` changed** — **no cross-repo drift event**, and the android repo's
  vendored pin is unaffected.

- **Board, via the GitHub MCP server** (the android probe's §6 MANUAL limit is that script's, not
  the session's): engine **#58** (audit F01/F02) and **#26** (SBOM) open and draft — unchanged —
  **plus #60, mine, draft**. Android **6 open, all draft**, still **zero merges in its whole
  history**. Read `merged_at`, never the rows' `merged` field, and for anything landed inside
  integration **#59** read the commit graph instead — `merged_at` is null there too.

- **The assigned S5 spec half is CLOSED and I re-verified it in the product** at `origin/main`, not
  from the records: `8575539`, `22b028e`, `7328a0b` are each ancestors of `origin/main`; §4.3.3
  carries `{product_id, acknowledged_at, order_id?}` with `order_id` **OPTIONAL**, under *"Decided
  2026-08-07 (gate PQ-A6-1, default-proceed)"*; §3.1's cap reads *"measured on the ciphertext"*; §3
  and §7.2 report every structural rejection as `decrypt_failed`, v1 deliberately adding no
  `malformed` code; `invalid-unknown-field.json` and both ack vectors are in the corpus. **The
  recurring prompt that assigns this slice is describing a state that ended on 2026-08-09**, and it
  still cites the stale pin `679a317` (real pin `11bb1f5`).

- **Next intent:** nothing claimed. #60 waits on `-IncludePublish` / `-IncludePackage` and on the
  decision to merge — both owner actions, not a firing's. If you want any file it touches, take it
  — I rebase.

- **Iteration 222, in one line.** Re-verified the assigned S5 spec half **first-person at
  `origin/main` `14469ad`**, not from the records: `node docs/sync-vectors/generate.mjs --check` →
  **`OK: 30 vector files match the generator.`**, exit **0**; §4.3.3 `entitlement_ack` present;
  `entitlement-ack.json`, `entitlement-ack-no-order-id.json` and `invalid-unknown-field.json` all
  in the corpus; §3.1's cap reads *"measured on the ciphertext"* and §7.2:1112 reports every
  structural rejection as `decrypt_failed`. The android repo's vendored `core/src/test/resources/
  sync-vectors/v1` is **`diff -r` identical** to the engine's — **no cross-repo drift event**.
  **No gate ran here and none is claimed** (dotnet/pwsh/sdkmanager/adb ABSENT, `ANDROID_HOME`
  UNSET); 221's green Windows CI run is **221's measurement, not mine**. **Eighteenth escalation
  withheld**: 221 sent the seventeenth **today** and no positive trigger fired since. **B-29
  re-measured and UNCHANGED** — `careerseeker-android` still `private:false` / `visibility:public`,
  `updated_at` still **2026-09-04T17:33:24Z**; open, already sent at run 203, and the owner's call,
  **not flipped by me**.
