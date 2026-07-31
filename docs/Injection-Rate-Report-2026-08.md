# Prompt-Injection Signal Rate Report — 2026-08

Measurement executed 2026-07-30 MDT / 2026-07-31 UTC by Terra.

## Result

The bounded measurement executed five discovery-only Scout cycles across
Greenhouse, Lever, and Ashby. It discovered 61 postings, quarantined 14,
rejected 47, drafted 0, and recorded 0 cycle errors. The observed
**quarantine-signal rate was 14/61 = 22.95%**.

That is not an estimate of malicious prompt-injection prevalence. Manual
context review found that all 14 flags were ordinary job-responsibility prose
matched by the broad `role_reassign` expression. In this small sample, the
flagged-set false-positive rate was therefore **14/14 (100%)**. The sample is
one non-empty board at one point in time and is not statistically
representative.

## Method and limits

All cycles used `run --once --dry-run --llm fake`; therefore no Gmail draft,
provider call, application send, or other external side effect was possible.
Public ATS reads were the only network activity. Each cycle used a 90-second
discovery ceiling and 30-second request ceiling.

The first Lever and Ashby cycles returned zero postings and exposed an
observability defect: empty feeds persisted `[]` instead of the configured
board identifier. The feed now supplies its configured identity independently
of results. One bounded re-run of each board verified that zero-result cycles
persist `Lever:mistral` and `Ashby:deel`. Those re-runs brought the bounded
measurement to five total cycles; no sixth cycle was run.

| Board | Cycles | Discovered | Quarantined | Rejected | Drafted | Errors | Reason codes |
|---|---:|---:|---:|---:|---:|---:|---|
| `Greenhouse:remotecom` | 1 | 61 | 14 | 47 | 0 | 0 | `role_reassign`: 14 |
| `Lever:mistral` | 2 | 0 | 0 | 0 | 0 | 0 | none |
| `Ashby:deel` | 2 | 0 | 0 | 0 | 0 | 0 | none |
| **Total** | **5** | **61** | **14** | **47** | **0** | **0** | `role_reassign`: 14 |

The zero-result Lever and Ashby observations mean this run cannot compare
classifier behavior across ATS sources. They are reported as measured, not
replaced with unexecuted or historical counts.

## Five anonymized flagged patterns

These are short pattern abstractions, not full postings. Bracketed terms remove
role, country, product, and company details.

1. “Act as a true subject-matter expert for [region].”
2. “Act as the main point of contact for [customers and stakeholders].”
3. “Act as a recognized subject-matter expert who drives [decisions].”
4. “Act as the ultimate authority on [data quality].”
5. “Act as a key decision-maker in [tool selection].”

All five are directives to a human employee describing job duties, not
instructions addressed to an AI system. Review of the remaining nine flagged
contexts found the same benign construction.

## False-positive assessment and proposed tuning

The current `role_reassign` pattern includes bare `act as a|an|the`. In job
descriptions, that phrase is common responsibility language, so using it alone
as a quarantine trigger is too broad.

Recommended change for Brandon's decision, **proposed only and not applied**:

- Keep the existing fail-closed quarantine path unchanged.
- Do not quarantine on bare `act as a|an|the` alone.
- Quarantine `role_reassign` when the role-change phrase is AI-directed
  (`you are now`, `from now on you`, `pretend to be`) or when `act as` occurs
  with a second adversarial reason such as `system_prompt`, `ai_address`,
  `reveal_prompt`, or `ignore_previous`.
- Retain bare `act as` as a lower-severity telemetry signal for another
  measurement before deleting it from detection entirely.
- Add fixtures for both benign responsibility prose and adversarial
  AI-directed role reassignment before any threshold change ships.

No detector expression or threshold was changed during B4.

## Reproduction evidence

Run from the repository root:

```powershell
dotnet src\Engine\bin\Release\net8.0\SeekerSvc.Engine.dll run --once --dry-run --llm fake --board greenhouse:remotecom --db tmp\b4-measurement\remotecom\cycle.db --artifacts tmp\b4-measurement\remotecom\artifacts --jd-dir tmp\b4-measurement\remotecom\job-descriptions --discovery-timeout-seconds 90 --http-timeout-seconds 30 --max-drafts-per-cycle 0

dotnet src\Engine\bin\Release\net8.0\SeekerSvc.Engine.dll run --once --dry-run --llm fake --board lever:mistral --db tmp\b4-measurement\mistral-identified\cycle.db --artifacts tmp\b4-measurement\mistral-identified\artifacts --jd-dir tmp\b4-measurement\mistral-identified\job-descriptions --discovery-timeout-seconds 90 --http-timeout-seconds 30 --max-drafts-per-cycle 0

dotnet src\Engine\bin\Release\net8.0\SeekerSvc.Engine.dll run --once --dry-run --llm fake --board ashby:deel --db tmp\b4-measurement\deel-identified\cycle.db --artifacts tmp\b4-measurement\deel-identified\artifacts --jd-dir tmp\b4-measurement\deel-identified\job-descriptions --discovery-timeout-seconds 90 --http-timeout-seconds 30 --max-drafts-per-cycle 0

dotnet src\Engine\bin\Release\net8.0\SeekerSvc.Engine.dll export-audit --db tmp\b4-measurement\remotecom\cycle.db --out tmp\b4-measurement\remotecom\audit.json
```

The executed Remote.com export reported an intact audit chain, 188 events, one
cycle, 61 discovered, 14 quarantined, board
`["Greenhouse:remotecom"]`, and reasons `{"role_reassign":14}`. The executed
Lever and Ashby exports each reported an intact chain, one named persisted
cycle, and zero discovered/quarantined.
