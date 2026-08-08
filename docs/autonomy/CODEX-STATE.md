# Codex release-candidate rung state

Updated: 2026-08-07

This file is the merge-tracked rung ledger. The live heartbeat and current
file claims remain on `autonomy/codex-state:STATE.md`.

| Rung | State | Executed evidence |
|---|---|---|
| R0 | DONE | PR #19 merged as `d267e5e`; local full publish/package gate built with 0 warnings/0 errors and offline 407/0; both CI runs passed. One executable was structurally verified. |
| R1 | DONE | PR #20. Defect reproduced: nested 10/50/200-term profiles acted 8/120, 0/120, 0/120 (`159 passed, 4 failed`). Job-side `lexical-v2` produced 8/120 at all sizes, retained threshold 4.0, and preserved the healthy demo (`164 passed, 0 failed`). Offline/full gates passed at 412/0 with analyzer build 0/0 and one-exe package self-check; both initial CI runs passed. |
| R2 | PENDING | No R2 command evidence yet. |
| R3 | PENDING | No live drafting cycle executed. |
| R4 | PENDING | No R4 command evidence yet. |
| R5 | PENDING | No R5 command evidence yet. |
| R6 | PENDING | No R6 command evidence yet. |
| R7 | PENDING | Not eligible until R0–R6 are DONE or BLOCKED. |

## Standing boundary

No deploy, console mutation, email send, purchase, signing, install, secret
access, certificate/store mutation, reboot, scheduled-task registration,
off-repo site edit, force-push, history rewrite, or `.appdata`-original
mutation is authorized by this ledger. R3's single capped Gmail draft cycle
remains gated on R1 and R2 being DONE.
