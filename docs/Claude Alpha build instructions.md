# Claude Alpha Build Instructions

Purpose: take CareerSeeker from the current Codex handoff state to a verified alpha release candidate that Codex can audit on Friday.

Start by reading `docs/Codex-Resume-Handoff.md` completely. Treat it as the current source of truth for what Codex finished, what was verified, and what still needed attention.

## Target Stop Point

Stop at a verified release-candidate source state, not merely "compiled source" and not only a standalone `.exe`.

The desired stopping point is:

1. Source code is complete for the alpha scope and committed.
2. Default alpha verification passes.
3. Publish verification passes.
4. Package verification passes.
5. A trusted-tester ZIP can be produced locally from the committed source.
6. Documentation and handoff notes record exactly what changed, what passed, what was skipped, and why.
7. Work is pushed to GitHub on a reviewable branch with a draft PR ready for Friday audit.

The executable is important proof that the alpha can run, but the source branch is the real deliverable. The `.exe` and ZIP should be reproducible from committed source.

## Branching

Do not continue directly on the existing Codex branch unless explicitly instructed.

Recommended branch flow:

```powershell
git switch agent/repo-cleanup
git pull
git switch -c claude/alpha-finish
git push -u origin claude/alpha-finish
```

Open a draft pull request from `claude/alpha-finish`.

Preferred target: `agent/repo-cleanup`, because this gives Codex a clean Friday diff from Claude's work.

Acceptable alternate target: `main`, if project ownership decides the alpha branch should go straight toward the mainline.

Do not merge the draft PR. Leave it for Codex audit and patching on Friday.

## GitHub Guidance

Push work to GitHub as you go. Do not keep the alpha finish work local-only.

Use small, reviewable commits with clear messages. A pushed branch plus draft PR is safer than unpushed local work because it gives rollback points, preserves CI history, and makes the Friday audit easier.

Do not commit secrets or local private state, including:

- `.env`, `.env.secret`, `env.secret`, or other secret files
- OAuth client JSON files
- token vaults
- provider key vaults
- `.appdata`
- `output`
- generated package ZIPs
- private resumes, cover letters, or job-search documents
- any file containing live API keys, refresh tokens, access tokens, or user account data

## Verification Commands

Run the broad verification suite before handoff:

```powershell
scripts\Verify-Alpha.ps1
```

Run publish verification:

```powershell
scripts\Verify-Alpha.ps1 -IncludePublish
```

Run package verification:

```powershell
scripts\Verify-Alpha.ps1 -IncludePackage
```

If touching live provider wiring, Gmail, Brave research, dashboard security, packaging, storage, or alpha lifecycle behavior, also run the relevant focused checks or live-safe verifier options described in `docs/Codex-Resume-Handoff.md`.

Prefer dry-run and live-safe helpers unless the user explicitly asks you to create real Gmail drafts or send external requests with side effects.

## Areas That Need Extra Care

Be especially careful with these alpha surfaces:

- BYOK provider wiring for Tailor and Gate
- Gmail draft creation and attachment handling
- ATS-clean resume PDF rendering
- package import/export safety
- local dashboard document routes
- secret handling and provider readiness checks
- Brave research URL filtering and fetch boundaries
- SQLite storage parity and lifecycle behavior

If any of these areas are changed, update or add focused harness coverage rather than relying only on broad smoke tests.

## End-of-Session Handoff

Before stopping, update `docs/Codex-Resume-Handoff.md` with:

- branch name and PR URL
- commit list or latest commit SHA
- concise summary of changed areas
- exact verification commands run
- exact pass/fail results
- checks intentionally skipped and the reason
- generated package or executable status
- remaining risks, known gaps, and suggested Codex audit focus

Also note whether any local-only files are required to reproduce the result, without revealing secret values.

## Final Instruction

The right finish line is a pushed, reproducible, verified alpha release-candidate source branch with a draft PR and updated handoff notes. Do not merge. Do not leave important work only on the local machine. Do not include secrets in the repository.
