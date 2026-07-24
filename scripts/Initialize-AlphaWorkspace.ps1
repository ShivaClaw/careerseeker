param(
    [switch] $DryRun,
    [switch] $RunDoctor,
    [switch] $RequireGmail,
    [switch] $RequireByok,
    [string] $Configuration = "Release",
    [string] $DbPath = ".appdata/careerseeker-alpha.db",
    [string] $ArtifactsPath = ".appdata/artifacts",
    [string] $JobDescriptionDirectory = ".appdata/job-descriptions",
    [string] $ProfileTemplatePath = ".appdata/profile.template.json",
    [string] $SecretsPath = "secrets/env.secrets",
    [string] $GmailClientPath = "resources/google-client.json",
    [string] $GmailVaultPath = ".appdata/oauth/gmail-token.dpapi",
    [string] $ByokVaultPath = ".appdata/secrets/byok-keys.dpapi",
    [string] $OutputDirectory = "output"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$engineProject = "src/Engine/SeekerSvc.Engine.csproj"
$publishedExe = Join-Path $repoRoot "SeekerSvc.Engine.exe"
$template = @"
# CareerSeeker alpha provider keys.
# Leave values blank until you intentionally configure BYOK/live research.
# This file is ignored by Git.

ANTHROPIC_API_KEY=
GEMINI_API_KEY=
# Brave Search aliases also accepted: BRAVE_SEARCH_API, CAREERSEEKER_BRAVE_SEARCH_API_KEY.
BRAVE_SEARCH_API_KEY=
"@
$profileTemplate = @"
{
  "format": "careerseeker-alpha-profile-v1",
  "profile": {
    "name": "Jordan Lee",
    "email": "jordan@example.com",
    "headline": "Senior Software Engineer"
  },
  "claims": [
    {
      "kind": "Title",
      "text": "Senior Software Engineer",
      "confidence": "verified",
      "sourceDoc": "resume.pdf"
    },
    {
      "kind": "Skill",
      "text": "distributed systems",
      "confidence": "verified",
      "sourceDoc": "resume.pdf"
    },
    {
      "kind": "Metric",
      "text": "reduced p99 latency 30%",
      "confidence": "verified",
      "sourceDoc": "resume.pdf"
    },
    {
      "kind": "Other",
      "text": "I have built reliable distributed systems in Go",
      "confidence": "verified",
      "sourceDoc": "resume.pdf"
    }
  ]
}
"@

function Resolve-WorkspacePath {
    param([string] $Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function New-DirectoryIfMissing {
    param(
        [string] $Label,
        [string] $Path
    )

    $full = Resolve-WorkspacePath $Path
    if (Test-Path -LiteralPath $full) {
        Write-Host "OK      $Label`: $full"
        return
    }

    if ($DryRun) {
        Write-Host "CREATE  $Label`: $full"
        return
    }

    New-Item -ItemType Directory -Force -Path $full | Out-Null
    Write-Host "CREATED $Label`: $full"
}

function New-TemplateIfMissing {
    param([string] $Path)

    $full = Resolve-WorkspacePath $Path
    if (Test-Path -LiteralPath $full) {
        Write-Host "OK      env secrets file exists: $full"
        return
    }

    if ($DryRun) {
        Write-Host "CREATE  env secrets placeholder: $full"
        return
    }

    $dir = Split-Path -Parent $full
    if (-not [string]::IsNullOrWhiteSpace($dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    Set-Content -LiteralPath $full -Value $template -Encoding UTF8
    Write-Host "CREATED env secrets placeholder: $full"
    Write-Host "        Fill this file locally; do not paste or commit secret values."
}

function New-ProfileTemplateIfMissing {
    param([string] $Path)

    $full = Resolve-WorkspacePath $Path
    if (Test-Path -LiteralPath $full) {
        Write-Host "OK      profile template exists: $full"
        return
    }

    if ($DryRun) {
        Write-Host "CREATE  profile template: $full"
        return
    }

    $dir = Split-Path -Parent $full
    if (-not [string]::IsNullOrWhiteSpace($dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    Set-Content -LiteralPath $full -Value $profileTemplate -Encoding UTF8
    Write-Host "CREATED profile template: $full"
    Write-Host "        Edit it, then import it with the alpha executable."
}

function Test-CommandAvailable {
    param([string] $Command)

    return $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

function Invoke-Checked {
    param(
        [string] $Command,
        [string[]] $Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $Command $($Arguments -join ' ')"
    }
}

Push-Location $repoRoot
try {
    Write-Host "CareerSeeker alpha workspace initialization"
    Write-Host "  root: $repoRoot"
    if ($DryRun) {
        Write-Host "  dry run: no files or directories will be created"
    }

    New-DirectoryIfMissing "SQLite directory" (Split-Path -Parent $DbPath)
    New-DirectoryIfMissing "draft artifacts" $ArtifactsPath
    New-DirectoryIfMissing "job descriptions" $JobDescriptionDirectory
    New-DirectoryIfMissing "Gmail token vault directory" (Split-Path -Parent $GmailVaultPath)
    New-DirectoryIfMissing "BYOK key vault directory" (Split-Path -Parent $ByokVaultPath)
    New-DirectoryIfMissing "release/output directory" $OutputDirectory
    New-DirectoryIfMissing "secret config directory" (Split-Path -Parent $SecretsPath)
    New-TemplateIfMissing $SecretsPath
    New-ProfileTemplateIfMissing $ProfileTemplatePath

    Write-Host ""
    Write-Host "Configured local paths:"
    Write-Host "  db: $DbPath"
    Write-Host "  artifacts: $ArtifactsPath"
    Write-Host "  job descriptions: $JobDescriptionDirectory"
    Write-Host "  profile template: $ProfileTemplatePath"
    Write-Host "  env secrets: $SecretsPath"
    Write-Host "  Gmail client JSON: $GmailClientPath"
    Write-Host "  Gmail token vault: $GmailVaultPath"
    Write-Host "  BYOK key vault: $ByokVaultPath"
    Write-Host "  output: $OutputDirectory"

    if ($RunDoctor) {
        $doctorArgs = @(
            "doctor",
            "--db", $DbPath,
            "--artifacts", $ArtifactsPath,
            "--secrets", $SecretsPath,
            "--key-vault", $ByokVaultPath,
            "--client", $GmailClientPath,
            "--vault", $GmailVaultPath
        )
        if ($RequireGmail) { $doctorArgs += "--require-gmail" }
        if ($RequireByok) { $doctorArgs += "--require-byok" }

        if (Test-Path -LiteralPath $publishedExe) {
            $doctorCommand = $publishedExe
            $doctorCommandArgs = $doctorArgs
        }
        else {
            if (-not (Test-CommandAvailable "dotnet")) {
                throw "dotnet is required to run doctor from source."
            }

            $doctorCommand = "dotnet"
            $doctorCommandArgs = @(
                "run", "--project", $engineProject,
                "-c", $Configuration, "--"
            ) + $doctorArgs
        }

        Write-Host ""
        Write-Host "Running startup doctor..."
        Invoke-Checked $doctorCommand $doctorCommandArgs
    }
}
finally {
    Pop-Location
}
