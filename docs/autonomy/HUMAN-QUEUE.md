# Release-candidate human queue

Updated: 2026-08-07

This queue contains actions that require Brandon's decision or an embargoed
human execution step. It is not authorization for an agent to cross the
mission boundary.

## Q01 — Unblock R2 before any live Gmail drafting

Status: OPEN. Blocks R3.

Evidence: R2's Remote.com rehearsal measured 58 discovered, 46 scored, and 0
act-eligible; the second Mistral attempt returned no postings. R2 is BLOCKED
in `docs/autonomy/CODEX-STATE.md` and detailed in `docs/BETA-BLOCKED.md`.

Human decision on return:

1. Select one currently non-empty, engineering-heavy public ATS board for a
   fresh bounded migration-copy rehearsal, or direct a new controlled
   calibration against an approved captured corpus.
2. Do not authorize a threshold change merely to fit one volatile feed.
3. Require `act-eligible > 0`, source-database identity, and an intact
   hash-only audit export before changing R2 to DONE.

Read-only orientation commands:

```powershell
git fetch --all --prune
git show origin/main:docs/autonomy/CODEX-STATE.md
git show origin/main:docs/BETA-BLOCKED.md
```

## Q02 — R3 sole live Gmail cycle

Status: WAITING ON Q01. The one authorized live-cycle allowance is unused.

Do not execute or reconnect Gmail while R2 is BLOCKED. After R2 is DONE, a
fresh iteration may verify readiness without printing secrets, then execute
at most ten drafts once, leave them in Drafts, and record draft IDs only,
dashboard DRAFTED rows, and the intact audit chain. Nothing may be sent.

If Gmail auth is unavailable then, the smallest human unblock is "reconnect
Gmail on return"; agents must not change OAuth console configuration.

## Q03 - Configure and execute production signing

Status: OPEN. Human-only Azure identity, billing, RBAC, repository-permission,
and signing work. R4 prepared and tested the offline flow; no resource was
created and no artifact was signed.

Choose values deliberately, then execute the resource commands in a human
Azure session. `eastus` and its endpoint are paired below; change both when
choosing another supported region.

```powershell
$SubscriptionId = '<AZURE-SUBSCRIPTION-ID>'
$ResourceGroup = 'careerseeker-signing'
$Location = 'eastus'
$Endpoint = 'https://eus.codesigning.azure.net/'
$SigningAccount = '<GLOBALLY-UNIQUE-3-TO-24-CHAR-NAME>'
$ProfileName = 'CareerSeekerPublicTrust'

az login
az account set --subscription $SubscriptionId
az provider register --namespace Microsoft.CodeSigning
if ((az provider show --namespace Microsoft.CodeSigning --query registrationState -o tsv) -ne 'Registered') {
  throw 'Microsoft.CodeSigning is not registered.'
}
az extension add --name artifact-signing --upgrade
az group create --name $ResourceGroup --location $Location
az artifact-signing create -n $SigningAccount -l $Location -g $ResourceGroup --sku Basic
az artifact-signing show -n $SigningAccount -g $ResourceGroup
```

Stop for the portal-only identity-validation flow. Confirm billing, geography,
legal identity, and the certificate-subject preview. Do not create a profile
until validation reports `Completed`. Then copy the Identity validation Id and
the exact subject preview without placing either in application logs.

```powershell
$IdentityValidationId = '<COMPLETED-IDENTITY-VALIDATION-ID>'
$ExactPublisher = '<EXACT-CERTIFICATE-SUBJECT-PREVIEW>'
$SignerObjectId = '<OIDC-APP-OR-SERVICE-PRINCIPAL-OBJECT-ID>'

az artifact-signing certificate-profile create `
  -g $ResourceGroup `
  --account-name $SigningAccount `
  -n $ProfileName `
  --profile-type PublicTrust `
  --identity-validation-id $IdentityValidationId
az artifact-signing certificate-profile show -g $ResourceGroup --account-name $SigningAccount -n $ProfileName

$ProfileScope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.CodeSigning/codeSigningAccounts/$SigningAccount/certificateProfiles/$ProfileName"
az role assignment create `
  --assignee $SignerObjectId `
  --role 'Artifact Signing Certificate Profile Signer' `
  --scope $ProfileScope
```

Configure a GitHub OIDC federated credential for the approved repository and
workflow. The manual Windows workflow must have `id-token: write` and
`contents: read`, build with the exact certificate subject, then use this
current signing shape (action names and inputs are intentionally pinned):

```yaml
- name: Azure login
  uses: azure/login@v3
  with:
    client-id: ${{ secrets.AZURE_CLIENT_ID }}
    tenant-id: ${{ secrets.AZURE_TENANT_ID }}
    subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

- name: Sign the single MSIX
  uses: azure/artifact-signing-action@v2
  with:
    endpoint: ${{ vars.ARTIFACT_SIGNING_ENDPOINT }}
    signing-account-name: ${{ vars.ARTIFACT_SIGNING_ACCOUNT }}
    certificate-profile-name: ${{ vars.ARTIFACT_SIGNING_PROFILE }}
    files: ${{ github.workspace }}\output\release\CareerSeeker-beta-win-x64.msix
    file-digest: SHA256
    timestamp-rfc3161: http://timestamp.acs.microsoft.com
    timestamp-digest: SHA256
```

The workflow's package and post-sign verification commands are:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Package-BetaRelease.ps1 -Publisher $ExactPublisher
powershell -ExecutionPolicy Bypass -File scripts\Test-BetaReleasePackage.ps1 `
  -PackagePath output\release\CareerSeeker-beta-win-x64.msix `
  -ExpectedPublisher $ExactPublisher `
  -RequireSigned
Get-AuthenticodeSignature output\release\CareerSeeker-beta-win-x64.msix |
  Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate
Get-Item output\release\CareerSeeker-beta-win-x64.msix | Select-Object FullName, Length
Get-FileHash output\release\CareerSeeker-beta-win-x64.msix -Algorithm SHA256
```

Official references:

- https://learn.microsoft.com/azure/artifact-signing/quickstart
- https://learn.microsoft.com/azure/artifact-signing/tutorial-assign-roles
- https://github.com/Azure/artifact-signing-action
- https://learn.microsoft.com/windows/msix/package/signing-package-overview

## Q04 - Execute the disposable-VM install matrix

Status: WAITING ON Q03. Human-only install, startup enablement, reboot,
uninstall, and separately confirmed data-deletion observations.

Initialize the evidence file only after the signed artifact exists. This
command verifies the signature and exact publisher before writing eleven
`PENDING` steps; it does not install anything.

```powershell
$Artifact = 'output\release\CareerSeeker-beta-win-x64.msix'
$ExactPublisher = '<EXACT-CERTIFICATE-SUBJECT-PREVIEW>'
$SignedHash = (Get-FileHash -LiteralPath $Artifact -Algorithm SHA256).Hash
powershell -ExecutionPolicy Bypass -File scripts\New-BetaVmInstallMatrix.ps1 `
  -ArtifactPath $Artifact `
  -ExpectedSha256 $SignedHash `
  -ExpectedPublisher $ExactPublisher `
  -OutputPath output\release\Beta-VM-Install-Matrix.md
```

Copy the exact MSIX and matrix to a disposable Windows 11 VM. Execute VM01
through VM11 in order, paste command/observation output into each slot, attach
screenshot IDs, and change a result from `PENDING` only after observing it.
Package removal and full-data deletion remain separate confirmations.

## Q05 - Publish and re-download the signed Beta

Status: WAITING ON Q03 and Q04. Human-only R2 upload and public distribution.
The established bucket is `careerseeker`; use a new versioned object and never
overwrite the Alpha object.

Wrangler 4.112.0 is present in the prior evidence. The commands below avoid
the unsupported `wrangler r2 object list` path. They require the existing R2
token to be intentionally loaded into the human process; do not print it.

```powershell
$Version = '<BETA-VERSION>'
$ExactPublisher = '<EXACT-CERTIFICATE-SUBJECT-PREVIEW>'
$Artifact = (Resolve-Path 'output\release\CareerSeeker-beta-win-x64.msix').Path
$SignedHash = (Get-FileHash -LiteralPath $Artifact -Algorithm SHA256).Hash
$ObjectKey = "beta/CareerSeeker-beta-$Version-win-x64.msix"
$Downloaded = "output\release\downloaded-CareerSeeker-beta-$Version-win-x64.msix"

if (-not (Test-Path Env:\CLOUDFLARE_R2_API_TOKEN)) {
  throw 'CLOUDFLARE_R2_API_TOKEN is not loaded in this human process.'
}
$env:CLOUDFLARE_API_TOKEN = $env:CLOUDFLARE_R2_API_TOKEN
try {
  wrangler r2 object put "careerseeker/$ObjectKey" --file="$Artifact" --content-type=application/msix --remote
  wrangler r2 object get "careerseeker/$ObjectKey" --file="$Downloaded" --remote
} finally {
  Remove-Item Env:\CLOUDFLARE_API_TOKEN -ErrorAction SilentlyContinue
}

$DownloadedHash = (Get-FileHash -LiteralPath $Downloaded -Algorithm SHA256).Hash
if ($DownloadedHash -ne $SignedHash) { throw 'Downloaded R2 object hash mismatch.' }
powershell -ExecutionPolicy Bypass -File scripts\Test-BetaReleasePackage.ps1 `
  -PackagePath $Downloaded `
  -ExpectedPublisher $ExactPublisher `
  -RequireSigned
```

Then update the approved download metadata/site copy with the exact object
key, bytes, and SHA-256, deploy through runbook Section 1, download through the
public URL on a clean machine, and repeat the minimal install/onboarding/
uninstall-preservation smoke. Record the object key, public URL, deployment
ID, downloaded hash, and VM result. None of these actions was executed in R4.

Official Cloudflare command references:

- https://developers.cloudflare.com/r2/objects/upload-objects/
- https://developers.cloudflare.com/r2/reference/wrangler-commands/
