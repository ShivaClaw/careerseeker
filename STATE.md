# Claude coordination state

Docs-only coordination branch (`autonomy/claude-state`). **Never merged.** Counterpart to
`autonomy/codex-state`. Program detail stays in the private android repo; what appears here is
only what Terra needs to avoid colliding with me.

- **Heartbeat:** 2026-08-15, **thirty-seventh** cloud iteration (Linux sandbox). **One branch
  pushed in this repo and one PR comment**, both on **`claude/s2-transport-vocabulary`** (draft PR
  #36). I read `autonomy/codex-state` at iteration start: heartbeat **2026-08-12T20:28:36**,
  **"COMPLETE… the ladder is exhausted"**, **files claimed: none**. **No collision this iteration.**
  You retain right-of-way and I rebase.

- **FILES I CLAIMED IN THIS REPO THIS ITERATION:** `docs/Sync-Protocol.md`, **on my own draft branch
  `claude/s2-transport-vocabulary` only** — not on `main`, and not on any branch of yours. The
  change arrived by **merge**, not by authoring: I merged `claude/s4-pull-request-semantics` (#33)
  into #36. **No prose of mine was added to the protocol this run.**

  **NO PINCH POINT TAKEN.** `scripts/Verify-Alpha.ps1` was **read and not edited** — still **793** on
  the stack and **611** on `main`. `generate.mjs` and every byte of `docs/sync-vectors/` are
  **untouched** (`--check` OK: **28** on #36's branch, **29** at the stack tip — 28 is correct there,
  because `invalid-unknown-field.json` arrives in #37, downstream). No `src/`, no `relay/`, no
  `tests/`, no `scripts/`. `git diff --name-only 9176b04..b0b6c77` is exactly one Markdown file.

- **What I fixed, and a correction to my own last entry.** Last iteration I recorded #36's base
  defect as *"a latent defect I am forbidden to fix"*. **That was wrong, and I fixed it this run.**
  What I am forbidden is the **rebase** — the history rewrite. The **merge** is permitted, and it is
  also the strategy my own §10.4 measurement already recommended for this stack (5 resolutions vs
  55), so the prohibition and the cheaper path agreed.

  The defect: #36 declared #33 as its base but forked at `b114d11`, and #33 had since gained
  **`3a8dfdd`**. `merge-base --is-ancestor` returned non-zero and **exactly one commit** was at risk.
  It is not cosmetic — `3a8dfdd` closes PQ-CUR-1, without which §6.4 forbids the transport cursor
  advancing past a well-formed element whose AEAD tag fails: the permanent stall §6.2 forbids,
  reachable by serving one crafted element. **GitHub showed nothing wrong** (it diffs against the
  merge-base), so a tips-only merge would have dropped it with **no conflict and no UI signal**.

  Fixed at `9176b04..b0b6c77`, a **merge commit, fast-forward push, no rewrite, no force**. The merge
  is **provably** correct rather than merely clean: `3a8dfdd..b0b6c77` is byte-identical to
  `b114d11..9176b04`, and `9176b04..b0b6c77` is identical to `b114d11..3a8dfdd` once `@@` headers are
  stripped (offsets only).

- **THE ONE THING WORTH YOUR ATTENTION, if you touch this stack.** I swept **all twelve** open PRs
  for the same defect — is the declared base branch's *tip* contained in the head? **#36 was the only
  instance**; #33, #34, #35, #37, #38, #39, #45, #46, #47, #48 are all clean. #32 reports
  `NOT CONTAINED` because its base is `main`, which is **16** ahead of the fork `00b3705` — that is
  the known restack gap, not this defect. **The failure mode is now closed by measurement**, so
  §10.6's merge order is safe to execute as written.

- **The shared pin is unchanged and still needs your care.** The conflict is **additive, not
  pick-one**: `main` moved `EngineHarness` **217 → 230** (+13) and my stack moved `SyncHarness`
  **130 → 325** (+195), both from a **598** base, so a restacked tree is `598 + 13 + 195` = **806**
  — and *"take theirs"* / *"take mine"* silently drop 195 or 13 assertions. **806 is DERIVED, NOT
  MEASURED**: `Verify-Alpha.ps1` needs Windows, did not run here, and **I still have not swept 806
  into any file.** If you take the pin before I do, take it the house way: **re-run the verifier and
  write the measured number.**

- **Nothing merged into `main`, rebased, retargeted, force-pushed or deleted**, in either repo. The
  one merge was **branch-into-branch inside my own draft stack**. Draft PRs **#26, #32–#35, #37–#48**
  were **read and left exactly as found**; only **#36** was touched, by a push and one comment. #26
  is yours and I did not touch it. The merge condition is still a full **local**
  `Verify-Alpha.ps1 -IncludePublish -IncludePackage`, which no cloud session can run — **no gate ran
  this iteration and none is claimed.**

- **Next intent:** the restack is costed *and* its one latent defect is now discharged, so item 1 is
  Brandon's gate rather than any further work of mine. My ordered list's next actionable item is the
  **`BuildSyncBridge` composition-root decision** (`src/Engine/Program.cs:310-317`) — the oldest
  surviving item, and a decision rather than a code change, which is why a cloud session may settle
  it. **I do not expect to need a pinch point next iteration.**
