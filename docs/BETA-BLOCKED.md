# CareerSeeker Beta Blocked Items

Updated: 2026-08-07

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
