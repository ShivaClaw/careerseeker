# CareerSeeker dependency and SBOM inventory

Snapshot: 2026-08-08 UTC  
Source base: `3a89fb58673712ac46aff82b35d7d269cb15793c`  
Machine-readable artifact: `docs/Dependency-SBOM.spdx.json` (SPDX 2.3 JSON)  
Snapshot SHA-256: `C63D89C84412F85E8004B57A684AD32FFFF66CAC5BEF14D5493825DFEE1BF1C5`

## Scope and method

This inventory covers every NuGet package resolved by the 23 projects in
`CareerSeeker.sln` plus the separate `tools/WindowsSdkTools` packaging project.
`scripts/New-DependencySbom.ps1` restores the solution, restores the build tool
in locked mode, reads `dotnet list package --include-transitive --format json`,
and joins that graph to the locally cached NuGet nuspec and SHA-512 metadata.
The committed SPDX document records package URLs, Package URLs (purls), exact
versions, SHA-512 content hashes, declared license expressions when the nuspec
provides one, project usage, and direct/transitive/build-only scope.

The offline verifier runs the generator with `-NoRestore -ValidateOnly` after
the normal build has restored assets. Validation is byte-for-byte: any package,
version, project-use, license-expression, or content-hash drift fails until the
SPDX snapshot and this review are deliberately refreshed.

## Measured NuGet graph

| Package | Version | CareerSeeker scope | Nuspec license declaration |
|---|---:|---|---|
| `Microsoft.Data.Sqlite` | 8.0.11 | Direct runtime | MIT |
| `Microsoft.Data.Sqlite.Core` | 8.0.11 | Transitive runtime | MIT |
| `Microsoft.Windows.SDK.BuildTools` | 10.0.26100.7705 | Build-only | NOASSERTION; nuspec URL requires separate license review |
| `PdfPig` | 0.1.15 | Direct runtime | Apache-2.0 |
| `SQLitePCLRaw.bundle_e_sqlite3` | 2.1.12 | Direct runtime | Apache-2.0 |
| `SQLitePCLRaw.core` | 2.1.12 | Transitive runtime | Apache-2.0 |
| `SQLitePCLRaw.lib.e_sqlite3` | 2.1.12 | Transitive runtime | Apache-2.0 |
| `SQLitePCLRaw.provider.e_sqlite3` | 2.1.12 | Transitive runtime | Apache-2.0 |
| `System.Memory` | 4.5.3 | Transitive runtime | NOASSERTION; legacy nuspec URL requires separate license review |

Measured totals: **9 unique packages** = **3 direct runtime**, **5 transitive runtime**,
and **1 build-only**. Seven packages expose SPDX license expressions;
two expose only legacy license URLs, so the SPDX document intentionally records
`NOASSERTION` rather than inferring a license.

## NuGet advisory evidence

The following NuGet-metadata queries were executed on 2026-08-08 UTC after a
successful restore:

```powershell
dotnet list CareerSeeker.sln package --vulnerable --include-transitive --format json
dotnet list tools\WindowsSdkTools\WindowsSdkTools.csproj package --vulnerable --include-transitive --format json
```

Both commands exited 0, reported `https://api.nuget.org/v3/index.json` as the
source, and returned no package with a vulnerability entry. This means **zero known NuGet advisories were reported for this snapshot**.
It is a dated metadata result, not a promise that future advisories cannot affect these versions.

## Reproducibility and lock boundary

- The three application `PackageReference` versions are exact in the project
  files, but the application projects do not currently commit `packages.lock.json`.
  Their five transitive versions are therefore recorded and CI-drift-checked by
  this SPDX snapshot, but they are not restore-lock-enforced.
- `Microsoft.Windows.SDK.BuildTools` is exact and lock-enforced by
  `tools/WindowsSdkTools/packages.lock.json`; the generator restores it with
  `--locked-mode`.
- Package SHA-512 values come from NuGet's local `.nupkg.sha512` metadata and
  are converted to SPDX hexadecimal form. The inventory does not copy package
  payloads into the repository.

Regenerate and then validate from the repository root:

```powershell
scripts\New-DependencySbom.ps1
scripts\New-DependencySbom.ps1 -NoRestore -ValidateOnly
Get-FileHash docs\Dependency-SBOM.spdx.json -Algorithm SHA256
```

## D08 evidence boundary

The resolved package set contains database, PDF parsing, compatibility, native
SQLite, and Windows packaging components; it contains no package presented by
this inventory as an analytics, advertising, or tracking SDK. That observation
does **not** prove the public D08 sentence. NuGet packages are only one possible
source of network behavior, and package names alone are not runtime evidence.

D08 remains **UNPROVEN** until a published binary/network-destination audit and
a deployed-site tracker scan are both executed and pinned. This SBOM also does
not inventory the .NET/Windows platform itself, operating-system DLLs, remote
ATS/Gmail/search/model services used intentionally by application code, or the
production site's served responses. Those are separate runtime/deployment
evidence surfaces.
