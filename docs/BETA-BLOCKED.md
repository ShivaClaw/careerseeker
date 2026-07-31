# CareerSeeker Beta Blocked Items

Updated: 2026-07-30

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
