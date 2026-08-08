# R0-R7 release-candidate ladder

| Rung | Outcome required |
| --- | --- |
| R0 | Fresh `origin/main` full gate; record build, 407/0 offline total, one executable, and measured MSIX bytes/SHA-256. Bootstrap mission, ladder, root agent pointer, and Codex state branch. |
| R1 | Calibrate lexical scoring across ~10/~50/~200-term profiles, pin defect first, correct job-side scoring, derive a sane Act threshold, and document distributions. |
| R2 | On a migration-copy database with a realistic 150+-term imported fixture, run one bounded public ATS `--once --dry-run` cycle and evidence a nonzero act-eligible funnel. |
| R3 | After R1/R2 green, execute the sole authorized live Gmail cycle (maximum ten drafts), leave drafts unsent, and record IDs, dashboard truth, and audit-chain evidence. |
| R4 | Prepare only: validate signing/package scripts offline, provide a disposable-VM matrix script, and put exact human commands in `HUMAN-QUEUE.md`. No signing, store change, or install. |
| R5 | Stage repo-only Beta distribution copy, migration guide, changelog, docs-site download text, and refreshed Positioning references. No deployment. |
| R6 | Close in-app confirmed full-data deletion, document dependency/SBOM evidence, run PSScriptAnalyzer, and finish remaining ordered hardening work. |
| R7 | Only after R0-R6 are done/blocked: smallest-first fixture edge cases, doc-drift audit, test gaps, or dead code—no new initiatives. |

For each rung: perform one coherent slice, run the required gate, place actual
command evidence in the PR and `Codex-Resume-Handoff.md`, update state and the
human queue when needed, then stop. R3 is the sole exception that can create
external Gmail drafts; it never sends email.
