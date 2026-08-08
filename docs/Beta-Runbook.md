# CareerSeeker Beta human-action runbook

Updated: 2026-07-30
Owner: Brandon
Rule: this is the single ordered Sunday list. Every step changes an external account, production service,
or tester machine and therefore requires a human. Terra executed none of these actions.

## 0. Merge gate

1. Review the B8 PR and `docs/Positioning.md`, especially every `UNPROVEN` row.
2. Confirm both GitHub CI runs are green.
3. Re-run from a clean worktree:

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 `
     -IncludePublish -IncludePackage
   ```

4. Confirm the reported artifact is one MSIX/one exe and the hash matches the artifact you intend to sign.
5. Merge normally; do not force-push or rewrite history.

## 1. Deploy the truth copy

Canonical review inputs:

- `docs-site/index.html`
- `docs-site/download.md` and `docs-site/download.html`
- `docs-site/privacy.md` and `docs-site/privacy.html`
- `docs-site/support.md` and `docs-site/support.html`
- `docs-site/autonomy-contract.md` and `docs-site/autonomy-contract.html`
- `docs/Positioning.md`

Human steps:

1. Diff each repository HTML file against its corresponding production source under
   `C:\Users\bkirk\Desktop\site-v2`. Do not copy Markdown residue into HTML.
2. Resolve the `UNPROVEN` wording decisions in `docs/Positioning.md`.
3. Port only the reviewed truth copy to the off-repo site source.
4. Run the site's local preview/tests.
5. Deploy through the existing Cloudflare Pages operator workflow.
6. Verify production, not just a per-deploy URL:

   - `https://careerseeker.app/`
   - `https://careerseeker.app/download/`
   - `https://careerseeker.app/privacy/`
   - `https://careerseeker.app/support/`
   - `https://careerseeker.app/autonomy-contract/`

7. Check visible version/date, links, Google Limited Use/no-training language, honest `gmail.compose`
   capability, Beta package/data-removal wording, and that the staged download page still says no Beta
   download is available until the signed artifact and disposable-machine evidence exist.
8. Record the production deployment id and exact verification output in the handoff.

Do not deploy if repository and production copy still disagree.

## 2. Protect `/api/signup`

First inspect Cloudflare Analytics for the exact production host/path and recent legitimate POST volume.
Cloudflare explicitly recommends validating the exact path and choosing a threshold from observed traffic.

Proposed starting rule for Brandon to approve:

| Setting | Value |
|---|---|
| Name | `CareerSeeker signup POST abuse` |
| Expression | `http.host eq "careerseeker.app" and http.request.uri.path eq "/api/signup" and http.request.method eq "POST"` |
| Counting characteristic | Source IP |
| Rate | 5 requests per 10 minutes |
| Mitigation | Block for 10 minutes |

Human steps:

1. Cloudflare dashboard → Security → WAF → Rate limiting rules.
2. Confirm the current plan supports the proposed period/action. If it does not, stop and document the UI's
   available choices; do not silently invent a weaker rule.
3. Create the rule with the exact POST path expression.
4. Verify one normal signup succeeds.
5. Use a non-production test IP/session to verify the threshold and JSON-facing UI failure remain understandable.
6. Check Security Events for the rule id and false positives after 24 hours; tune only from evidence.
7. Record rule id, threshold, action, and observation window in the handoff.

Official reference:
https://developers.cloudflare.com/waf/rate-limiting-rules/create-zone-dashboard/

## 3. Process the current OAuth test-user queue

The July 24 handoff said two entries were pending, but that count is stale. Derive current truth.

1. Run the operator script without `-Apply`:

   ```powershell
   powershell -ExecutionPolicy Bypass -File `
     C:\Users\bkirk\Desktop\Process-PendingOAuthTestUsers.ps1
   ```

2. Do not paste the returned addresses into source control, issues, or public logs.
3. In Google Cloud Console → Google Auth Platform → Audience → Test users, add only the currently returned
   addresses.
4. Confirm each address is present in the Console.
5. Only then clear those exact queue entries:

   ```powershell
   powershell -ExecutionPolicy Bypass -File `
     C:\Users\bkirk\Desktop\Process-PendingOAuthTestUsers.ps1 `
     -Apply -OnlyEmails '<address-1>','<address-2>'
   ```

6. Re-run without `-Apply`; expected result is no entries for the processed addresses.
7. Record counts only unless users explicitly consent to an address appearing in a private operator log.

This step changes Google Console and production KV. Never run `-Apply` before the Console additions succeed.

## 4. Submit OAuth production verification, then follow Google's CASA direction

`gmail.compose` is restricted and can manage/send drafts. CareerSeeker needs it to create and retain
user-reviewable Gmail drafts; the application-level no-send implementation does not make the scope
non-restricted.

Prepare:

- production homepage, privacy policy, support page, and Autonomy Contract;
- verified domain ownership and current project owner/editor contact details;
- exact installed/Desktop OAuth client and exact scope list (no new scopes);
- demo video showing onboarding consent, OAuth flow, draft creation, review, disconnect/revoke, and local data
  deletion;
- scope justification explaining why `gmail.send` cannot create/manage reviewable drafts;
- data-flow diagram: local Windows app → Google Gmail Drafts API; no CareerSeeker cloud processing of Gmail
  content;
- DPAPI/storage, deletion, incident-response, vulnerability-management, secure-development, and the
  dependency/SBOM evidence in `docs/Dependency-SBOM-Inventory.md` plus `docs/Dependency-SBOM.spdx.json`;
- `scripts\Verify-Alpha.ps1` and claims-register evidence.

Human steps:

1. In Google Auth Platform, verify branding, domains, policy URLs, contacts, audience, and the one existing
   scope.
2. Publish the OAuth app to Production only when the product/policies are ready for Google's review.
3. Choose Prepare for Verification and submit the scope justification and demo.
4. Respond to the OAuth review team from a monitored project-owner/editor mailbox.
5. Do not independently purchase or claim CASA completion. Google states that its review team contacts
   restricted-scope apps when it is time to begin the security assessment.
6. When invited, select an empanelled assessor, record the assigned assurance level/cost/timeline, complete
   remediation, and retain the Letter of Validation.
7. Calendar annual re-verification/assessment ownership.

Official references:

- https://support.google.com/cloud/answer/13461325
- https://support.google.com/cloud/answer/13464321
- https://support.google.com/cloud/answer/13465431

## 5. Configure production MSIX signing

Preferred path: Azure Artifact Signing after human identity, billing, subscription, and repository-permission
approval. A conventional trusted code-signing certificate remains the fallback.

1. Complete publisher identity verification and choose the paid signing resource.
2. Record the exact certificate subject/publisher identity.
3. Build the MSIX with `-Publisher` equal to that exact subject. The unsigned special OID must not remain in a
   production-signed manifest.
4. Configure CI secrets/permissions through the platform UI; never commit a PFX, password, client secret, or
   signing token.
5. Before any signing, exercise the no-certificate/no-write validation flow:

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\Sign-BetaRelease.ps1 `
     -PackagePath output\release\CareerSeeker-beta-win-x64.msix `
     -CertificatePath C:\secure\publisher.pfx `
     -ValidateOnly
   ```

6. Sign and timestamp the MSIX through the human-approved Azure Artifact
   Signing workflow. Q03 in `docs/autonomy/HUMAN-QUEUE.md` contains the exact
   Azure resource, RBAC, GitHub OIDC/action, build, and verification commands.
   The local PFX fallback, when intentionally used, is:

   ```powershell
   $env:CAREERSEEKER_SIGNING_PASSWORD = Read-Host -AsSecureString |
     ConvertFrom-SecureString -AsPlainText
   powershell -ExecutionPolicy Bypass -File scripts\Sign-BetaRelease.ps1 `
     -PackagePath output\release\CareerSeeker-beta-win-x64.msix `
     -CertificatePath C:\secure\publisher.pfx
   Remove-Item Env:\CAREERSEEKER_SIGNING_PASSWORD
   ```

7. Verify signature, exact publisher, and absence of the unsigned-package OID:

   ```powershell
   $ExactPublisher = 'CN=EXACT CERTIFICATE SUBJECT'
   powershell -ExecutionPolicy Bypass -File scripts\Test-BetaReleasePackage.ps1 `
     -PackagePath output\release\CareerSeeker-beta-win-x64.msix `
     -ExpectedPublisher $ExactPublisher `
     -RequireSigned
   Get-AuthenticodeSignature output\release\CareerSeeker-beta-win-x64.msix |
     Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate
   ```

8. Record signer subject, timestamp, artifact bytes, SHA-256, CI run, and verification output.

Official references:

- https://learn.microsoft.com/windows/msix/package/signing-package-overview
- https://learn.microsoft.com/azure/artifact-signing/quickstart
- https://learn.microsoft.com/azure/artifact-signing/tutorial-assign-roles
- https://github.com/Azure/artifact-signing-action

## 6. Disposable Windows installer matrix

First initialize the checklist from the exact signed artifact. This verifies
the signature and writes only a Markdown evidence template; it performs no
install or machine mutation:

```powershell
$Artifact = 'output\release\CareerSeeker-beta-win-x64.msix'
$ExactPublisher = 'CN=EXACT CERTIFICATE SUBJECT'
$Hash = (Get-FileHash -LiteralPath $Artifact -Algorithm SHA256).Hash
powershell -ExecutionPolicy Bypass -File scripts\New-BetaVmInstallMatrix.ps1 `
  -ArtifactPath $Artifact `
  -ExpectedSha256 $Hash `
  -ExpectedPublisher $ExactPublisher `
  -OutputPath output\release\Beta-VM-Install-Matrix.md
```

Then use a disposable Windows 11 tester VM or machine with no CareerSeeker
package registration and fill every recorded-output slot:

1. Install the exact signed artifact.
2. Confirm Start-menu name/icon and that Startup Apps shows CareerSeeker disabled.
3. Launch without a console and complete onboarding with a synthetic resume, manual provider, and Gmail skip.
4. Confirm `%LOCALAPPDATA%\CareerSeeker` is created and no draft/provider call occurs.
5. Close and relaunch; confirm the engine starts discovery-only and status is honest.
6. Enable startup manually, reboot, and confirm one engine instance plus local logging.
7. Pause, resume, and stop through the documented local controls.
8. Repeat upgrade-in-place from the previous signed Beta when one exists.
9. While the app is still installed, run `CareerSeeker.exe delete-all-data` once and confirm it reports
   `NOT DELETED`. Close every CareerSeeker process, copy the exact displayed path-bound phrase into
   `--confirm-delete-all-data`, and confirm `target exists after: no`.
10. Relaunch once to recreate a synthetic workspace sentinel, then uninstall the application. Confirm
    Start-menu/startup registration is removed while the external sentinel remains.

Do not claim install, uninstall, startup, or reboot support before this matrix is recorded.
Q04 in `docs/autonomy/HUMAN-QUEUE.md` is the exact handoff.

## 7. Publish the signed Beta artifact

Only after Sections 1, 2, 5, and 6:

1. Use a new versioned Beta object/path; do not overwrite the shipped Alpha ZIP.
2. Upload the exact signed MSIX through the existing human-controlled release/R2 workflow. Q05 in
   `docs/autonomy/HUMAN-QUEUE.md` pins the bucket, versioned object-key shape, Wrangler upload, download, hash,
   and signed-package re-verification commands. It intentionally does not use the unsupported
   `wrangler r2 object list` command.
3. Update download metadata/site links to the new version, bytes, and SHA-256.
4. Download it through the public path to a clean machine, verify the signature/hash, then repeat the minimal
   install/onboarding/uninstall preservation smoke.
5. Record object path, public URL, deployment id, signed hash, and tester result.

## 8. Closeout

The Sunday launch record is complete only when it contains:

- B8 merge commit and green CI links;
- production trust-copy deployment id and URL checks;
- signup rate-limit rule id and 24-hour review;
- OAuth queue before/after counts;
- OAuth verification submission id/state and CASA instruction state;
- signing identity, signed artifact hash, and CI run;
- disposable-machine install/reboot/uninstall evidence;
- public release object/path and downloaded-artifact verification.

Anything not executed remains `PENDING`; “configured” and “verified” are never synonyms.
