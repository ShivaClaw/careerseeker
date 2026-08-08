# Release-candidate human queue

Updated: 2026-08-07

This queue contains actions that require Brandon's decision or an embargoed
human execution step. It is not authorization for an agent to cross the
mission boundary.

## Q01 — Unblock R2 before any live Gmail drafting

Status: OPEN. Blocks R3.

Evidence: R2's Remote.com rehearsal measured 58 discovered, 46 scored, and 0
act-eligible; the second Mistral attempt returned no postings. R2 is BLOCKED
in `docs/autonomy/CODEX-STATE.md` and detailed in `docs/BETA-BLOCKED.md`.

Human decision on return:

1. Select one currently non-empty, engineering-heavy public ATS board for a
   fresh bounded migration-copy rehearsal, or direct a new controlled
   calibration against an approved captured corpus.
2. Do not authorize a threshold change merely to fit one volatile feed.
3. Require `act-eligible > 0`, source-database identity, and an intact
   hash-only audit export before changing R2 to DONE.

Read-only orientation commands:

```powershell
git fetch --all --prune
git show origin/main:docs/autonomy/CODEX-STATE.md
git show origin/main:docs/BETA-BLOCKED.md
```

## Q02 — R3 sole live Gmail cycle

Status: WAITING ON Q01. The one authorized live-cycle allowance is unused.

Do not execute or reconnect Gmail while R2 is BLOCKED. After R2 is DONE, a
fresh iteration may verify readiness without printing secrets, then execute
at most ten drafts once, leave them in Drafts, and record draft IDs only,
dashboard DRAFTED rows, and the intact audit chain. Nothing may be sent.

If Gmail auth is unavailable then, the smallest human unblock is “reconnect
Gmail on return”; agents must not change OAuth console configuration.
