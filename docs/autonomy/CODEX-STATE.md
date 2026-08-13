# Codex release-candidate rung state

Updated: 2026-08-12

This file is the merge-tracked rung ledger. The live heartbeat and current
file claims remain on `autonomy/codex-state:STATE.md`.

| Rung | State | Executed evidence |
|---|---|---|
| R0 | DONE | PR #19 merged as `d267e5e`; local full publish/package gate built with 0 warnings/0 errors and offline 407/0; both CI runs passed. One executable was structurally verified. |
| R1 | DONE | PR #20. Defect reproduced: nested 10/50/200-term profiles acted 8/120, 0/120, 0/120 (`159 passed, 4 failed`). Job-side `lexical-v2` produced 8/120 at all sizes, retained threshold 4.0, and preserved the healthy demo (`164 passed, 0 failed`). Offline/full gates passed at 412/0 with analyzer build 0/0 and one-exe package self-check; both initial CI runs passed. |
| R2 | BLOCKED | A retained migration copy preserved the source at 172,032 bytes and SHA-256 `0A5605…E18192`; a 31-claim/321-term fixture imported successfully. Remote.com measured 58 discovered, 12 quarantined, 46 scored/rejected, 0 act-eligible/drafted/errors; totals were 2.36–3.63. Mistral returned 0. Audit chain was intact. Two-attempt limit reached; see `docs/BETA-BLOCKED.md`. |
| R3 | BLOCKED | Fresh `origin/main` at `d486459` reports R2 BLOCKED, while both `R-LADDER.md` and `CODEX-MISSION.md` permit the sole live Gmail cycle only after R1/R2 are green/DONE. Two independent prerequisite reads agreed. No Gmail/token/secret access or live attempt occurred; the one-cycle allowance remains unused. See `docs/BETA-BLOCKED.md` and `docs/autonomy/HUMAN-QUEUE.md`. |
| R4 | DONE | PR #23. Signing validation proved no certificate/password read in `-ValidateOnly`; signed-package expectations matched an exact publisher, found no unsigned OID, and rejected an unsigned control under `-RequireSigned`; VM01-VM11 validation wrote nothing. Offline/full gates passed at 412/0 with analyzer build 0/0 and one-exe package self-check. Human signing, VM execution, and publish commands are Q03-Q05. |
| R5 | DONE | PR #24. Repository-only changelog, preservation-first migration guide, and truthful download Markdown/HTML are implemented. Import preview executed without import or overwrite. Offline/full gates are 412/0, analyzers 0/0, and the one-executable package self-check is green; both initial CI runs passed and the post-fetch rebase/full gate repeated green. |
| R6 | IN PROGRESS | R6(a) is complete on PR #25. R6(b) is BLOCKED on draft PR #26. R6(c) is complete on PR #40: PSScriptAnalyzer 1.25.0 initially found 374 items; 19 actionable automatic-variable/runspace findings were fixed, every remaining family is counted and justified, and the enforced pass is 0. Initial CI runs `31657569672` and `31657606281` passed both jobs; fresh main remained `00b3705`, rebase was a no-op, and the post-fetch analyzer/full gate repeated 0 findings and offline 598/0 with .NET analyzers 0/0 and a one-executable package self-check. R6(d) backlog review remains. |
| R7 | PENDING | Not eligible until R0–R6 are DONE or BLOCKED. |

## Standing boundary

No deploy, console mutation, email send, purchase, signing, install, secret
access, certificate/store mutation, reboot, scheduled-task registration,
off-repo site edit, force-push, history rewrite, or `.appdata`-original
mutation is authorized by this ledger. R3's single capped Gmail draft cycle
remains gated on R1 and R2 being DONE; R2 is BLOCKED, so no R3 live cycle is
currently authorized.
