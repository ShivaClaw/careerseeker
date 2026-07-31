# CareerSeeker Beta Audit Request

Updated: 2026-07-30

This is the adversarial review index for the Windows Beta milestone ladder.
Each claim below is limited to evidence executed by Terra in the session that
recorded it. Commands are written from the repository root on Windows.

## B0 - Preflight baseline

Branch: `codex/beta-M0-preflight`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-07-30 |
|---|---|---|
| Remote `main` baseline is `14a7dfec374cda410aa28b13c456d695f38e3507`. | `git fetch --all; git rev-parse origin/main` | Exact SHA matched. |
| The unmerged honesty-fix tip is `40bc9a7166afb7d9742d75ef1b93b2ce0c8f5c1b`. | `git fetch --all; git rev-parse origin/fix/engine-actually-runs` | Exact SHA matched. |
| Release build is warning/error clean on the B0 base. | `dotnet build CareerSeeker.sln -c Release --warnaserror` | `Build succeeded`, `0 Warning(s)`, `0 Error(s)`. |
| The pinned offline Alpha baseline is green at 341. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` | `Offline total: 341 passed, 0 failed`. |
| Local publish and package paths complete. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | Offline 341/0; published demo `errors: 0`; manifest and installed/Desktop OAuth checks passed; 50 checksums verified; dashboard and Alpha 2.0 setup smokes passed. |
| B0 changed documentation only and did not touch the frozen Android program. | `git diff --name-only origin/main...codex/beta-M0-preflight` | Expected after the B0 commit: only `docs/Codex-Resume-Handoff.md` and `docs/BETA-AUDIT-REQUEST.md`. |
| B0 did not change the pinned assertion total or count-bearing docs. | `git diff origin/main...codex/beta-M0-preflight -- scripts/Verify-Alpha.ps1 README.md src/Engine/README.md docs/CareerSeeker-Project-Summary.md docs/External-Audit-Handoff.md` | Expected: no output. |

### Scope exclusions

The package was built locally under ignored output paths. No Cloudflare,
Google Console, OAuth test-user, Play Console, email, purchase, new-scope,
off-repo site, relay, or Android action was performed. No live BYOK, Brave, or
Gmail smoke was part of B0.
