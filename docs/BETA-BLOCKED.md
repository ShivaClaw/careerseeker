# CareerSeeker Beta Blocked Items

Updated: 2026-08-07

## R6(b) dependency/SBOM inventory — PowerShell 7 CI byte drift

Scope: R6(b) prepared a source-dependency SPDX snapshot, byte-for-byte drift
validation, license/lock boundaries, and D08 evidence backstops. Local Windows
PowerShell 5.1 gates are green, but the PR cannot merge until both required CI
runs are green.

Two real CI attempts were made on PR #26:

1. Push run `31236649674` and pull-request run `31236667575` built with zero
   warnings/errors, then failed at the SPDX byte comparison under PowerShell 7.
   The first generator used PowerShell's compact `ConvertTo-Json` output.
2. The generator was changed to a deterministic serializer for its restricted
   string/boolean/array/ordered-map data model. The local artifact remained
   byte-identical at SHA-256
   `A82CE684EC660FC1FBB93FF0553F38D12722223E77A90243FBE071AC5C01D71E`,
   and offline verification again passed 418/0. Push run `31236744839` and
   pull-request run `31236746674` again built 0/0 and failed at the same byte
   comparison.

The two-attempt limit is reached. No third runner experiment was started and
PR #26 remains open and unmerged. The local graph is still evidenced as nine
packages, the post-publish Windows PowerShell 5.1 check is green, and D08
remains UNPROVEN; cross-host byte reproducibility is the blocked claim.

Smallest human unblock: authorize one diagnostic CI run that writes only the
generated SPDX SHA-256, byte length, and first differing byte offset (or uploads
the generated SPDX as a short-lived workflow artifact), compare it with the
committed 14,897-byte file, then fix the identified environment-dependent
field. Do not weaken validation to a semantic-only comparison and do not merge
PR #26 until both push and pull-request CI runs pass.

## R3 sole live Gmail drafting cycle — prerequisite R2 is not DONE

Scope: the 2026-08-07 authorization permits exactly one non-dry-run Gmail
drafting cycle, capped at ten drafts and leaving every draft unsent, only
after R1 and R2 are DONE/green.

Two independent read-only prerequisite checks were executed after the
iteration's mandatory `git fetch --all --prune`:

1. Fresh `origin/main` was
   `d4864590c38cd52a332349f20853423e477e9e0f`. Its merge-tracked
   `docs/autonomy/CODEX-STATE.md` reports R1 DONE and R2 BLOCKED after the two
   bounded public-board rehearsals produced zero act-eligible postings.
2. Fresh `docs/autonomy/R-LADDER.md` requires R1/R2 green, and
   `docs/autonomy/CODEX-MISSION.md` independently limits the live exception
   to after R1 and R2 are complete. Both therefore prohibit starting R3.

R3 is BLOCKED by its authorization prerequisite. No live attempt was made:
attempting Gmail auth, reading token state, or creating a draft would cross
the mission boundary rather than diagnose the prerequisite. The window's
single live-cycle allowance remains unused.

Smallest human unblock: first complete a fresh bounded R2 rehearsal with
`act-eligible > 0`, merge its evidence, and change R2 from BLOCKED to DONE.
Only a later fresh iteration may then check Gmail readiness and execute the
one capped live cycle. If Gmail auth is unavailable at that point, record the
separate auth block without touching OAuth configuration or retrying live.

## R2 real-profile public-ATS rehearsal — no posting cleared the calibrated Act rail

Scope: R2 acceptance requires a nonzero `act-eligible` result from a bounded
public-ATS `--once --dry-run` cycle on a verified migration copy containing a
realistic 150+-term profile. No drafting or Gmail/provider call was permitted.

The source database remained byte-identical before and after the rehearsal:
172,032 bytes, last-write UTC `2026-07-19T23:04:58`, SHA-256
`0A560528C486375383F1F84F1BA8EA1536B341C75C8BC5EF0CF3D1BEE4E18192`.
The retained backup API verified the copied database's integrity, current
schema, idempotent migration, and unchanged source. A synthetic resume-derived
fixture then replaced the copy's demo profile with 31 verified claims and 321
distinct rankable terms.

Two bounded public-read attempts were made on 2026-08-07 MDT / 2026-08-08 UTC:

1. `Greenhouse:remotecom` discovered 58, quarantined 12, scored/rejected 46,
   and produced 0 act-eligible, 0 acted, 0 drafted, and 0 errors. An offline
   read of the copied database measured totals 2.36–3.63 (mean 2.932); no
   scored row reached the calibrated 4.0 Act threshold.
2. `Lever:mistral` returned zero postings and therefore 0 act-eligible, 0
   drafted, and 0 errors.

The final hash-only audit export reported an intact chain, two named cycle
rows, 256 events, and no payloads. R2 is BLOCKED because its required
`act-eligible > 0` acceptance condition was not met. The global score rail was
not weakened to fit a volatile feed, and no third public cycle was run.

Smallest human unblock: after return, select one currently non-empty,
engineering-heavy public ATS board for a fresh bounded R2 rehearsal, or direct
a new controlled calibration against an approved captured job corpus before
authorizing any score/threshold change. R3 remains ineligible unless R2 is
subsequently marked DONE.

## B3 local Browser visual check — background dashboard process would not stay bound

Scope: optional visual inspection of the new `/jobs` score-component rendering in the in-app Browser.
This does not block B3 implementation or its automated renderer/HTTP evidence.

Three bounded attempts were made from the repository root against local port 7791 and the local-only
`tmp\beta-b3-browser\careerseeker.db`:

1. A hidden PowerShell holder started `dotnet` with redirected stdin. The holder exited and
   `Invoke-WebRequest http://localhost:7791/jobs` could not connect.
2. A hidden, base64-encoded PowerShell holder used `ProcessStartInfo.ArgumentList` and kept redirected
   stdin open. It also exited before the port bound; 20 bounded probes returned no response.
3. A hidden PowerShell pipeline kept stdin open with a long-running producer piped to the dashboard
   command. It likewise exited before binding; 20 bounded probes returned no response.

Afterward, process and TCP queries found no surviving dashboard listener. No browser result is claimed.
The local dashboard rendering path was still executed by:

```text
dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build
```

That run passed the assertion `dashboard job view surfaces encoded score components and lexical
rationale`, along with score ordering and persistence assertions. The full verifier subsequently
reported `380 passed, 0 failed`.

Human follow-up: run the foreground command below in an interactive console, then open `/jobs`:

```powershell
dotnet src\Engine\bin\Release\net8.0\SeekerSvc.Engine.dll dashboard --port 7791 --db tmp\beta-b3-browser\careerseeker.db --artifacts tmp\beta-b3-browser\artifacts
```
