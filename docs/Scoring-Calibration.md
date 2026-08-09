# Lexical scoring calibration

Updated: 2026-08-07

## Question

The former `lexical-v1` CV-match score divided matched profile-term weight by
the weight of the entire profile. That made the denominator grow whenever a
resume gained unrelated valid detail. The same posting could therefore score
lower solely because the candidate profile became richer.

## Repeatable method

`tests/EngineHarness/Program.cs` builds a deterministic 120-posting corpus:

- 8 targeted senior-software postings;
- 32 adjacent software-delivery postings;
- 80 unrelated marketing, finance, and operations postings.

Every posting has compensation, freshness, recruiter, domain, remote, and
description-length evidence held constant so the experiment isolates lexical
fit. The harness scores the same corpus against strictly nested profiles with
10, 50, and 200 distinct verified terms. The 10-term base is contained
unchanged in both richer profiles; additional terms do not occur in the
corpus. It then composes the semantic result through the production Scorer and
its default Act threshold.

This is a controlled synthetic calibration, not a claim about a production
job-market distribution. R2 separately requires a bounded public-ATS
dry-run with a realistic rich profile.

## Defect reproduction

With the new assertions present but before the formula change, the executed
EngineHarness run produced:

```text
CAL profile=10  corpus=120 cv=[1.50,4.08] total=[2.99,4.27] p50=2.99 p95=4.27 act=8
CAL profile=50  corpus=120 cv=[1.50,2.86] total=[2.99,3.85] p50=2.99 p95=3.85 act=0
CAL profile=200 corpus=120 cv=[1.50,2.63] total=[2.99,3.77] p50=2.99 p95=3.77 act=0
FAIL strictly richer profiles never lower the same posting's lexical score
FAIL calibration keeps act eligibility between three and fifteen percent at every profile size
FAIL calibration admits the targeted band and rejects adjacent and unrelated fixtures
FAIL calibrated rationale reports job-side coverage and ranker version
=== 159 passed, 4 failed ===
```

This pins the reported dead zone: 8/120 eligible with the small profile,
0/120 with both richer supersets.

## `lexical-v2` formula

The denominator is now the posting's rankable terms, not the profile's terms.
Each unique title term has placement weight 1.5 and each description-only term
has weight 1.0. A matched profile term contributes its placement weight times
`min(profileTermWeight / 3.0, 1.0)`, preserving stronger Skill/Title evidence
without letting it exceed full coverage. The final coverage blend is:

```text
combined = 0.70 * jobCoverage + 0.30 * titleCoverage
cvMatch  = 1.5 + 3.5 * combined
```

Because adding a claim can only introduce a match or increase an existing
term's maximum evidence weight, it cannot reduce the same posting's score.
Rationales now report `job coverage` and `title coverage`, and persisted rows
identify the ranker as `lexical-v2`.

## Post-fix distribution and Act threshold

The executed post-fix EngineHarness run produced identical distributions for
all three profile sizes:

| Distinct profile terms | CV range | Total range | P50 | P95 | Target minimum | Adjacent maximum | Act eligible |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 | 1.50–3.88 | 2.99–4.20 | 2.99 | 4.20 | 4.20 | 3.20 | 8/120 (6.7%) |
| 50 | 1.50–3.88 | 2.99–4.20 | 2.99 | 4.20 | 4.20 | 3.20 | 8/120 (6.7%) |
| 200 | 1.50–3.88 | 2.99–4.20 | 2.99 | 4.20 | 4.20 | 3.20 | 8/120 (6.7%) |

The targeted and adjacent fixture bands leave a 1.00-point observed gap. The
existing default Act threshold of 4.0 remains inside that gap, 0.20 below the
target minimum and 0.80 above the adjacent maximum. Retaining 4.0 yields a
6.7% eligibility rate at every profile size, within the mission's 3–15%
acceptance band, so no threshold change is justified by this calibration.

## Re-run

```powershell
dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release
scripts\Verify-Alpha.ps1
```

The first command must report `186 passed, 0 failed`; the second command pins
the repository-wide measured total and count-reporting documentation.
