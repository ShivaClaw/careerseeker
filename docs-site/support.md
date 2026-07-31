# CareerSeeker Support

**Last updated:** 2026-07-30

## Contact

| Channel | Address | Response target |
| --- | --- | --- |
| **General support** | support@careerseeker.app | 48 hours on business days |
| **Privacy inquiries** | privacy@careerseeker.app | 48 hours on business days |
| **Security reports** | security@careerseeker.app | 24 hours |
| **Product website** | https://careerseeker.app | Public product site |

During closed beta, support is provided by the development team directly. These are response targets, not a guaranteed service-level agreement.

## Current Beta Actions

### Disconnect Gmail

CareerSeeker L1 creates Gmail drafts through a local OAuth token vault. To disconnect the current beta:

1. Use the token-protected **Disconnect Gmail** control on the local dashboard when it is available.
2. From source or an advanced terminal, run `CareerSeeker.exe disconnect-gmail --vault .appdata/oauth/gmail-token.dpapi`.
3. Confirm removal on your [Google Account permissions page](https://myaccount.google.com/permissions) if you want to revoke the account grant as well.

After disconnection, CareerSeeker cannot create Gmail drafts until you reconnect and authorize again.

### Revoke LLM Provider Keys

From source or an advanced terminal, run `CareerSeeker.exe clear-byok --key-vault .appdata/secrets/byok-keys.dpapi` to delete the local DPAPI provider-key vault.

Also delete any provider keys from environment variables or `secrets/env.secrets` if you supplied them there. CareerSeeker does not retain copies of provider keys outside the local configuration you control.

An in-app provider-key manager is planned for the product shell.

### Delete Local Data

Current Windows-engine Beta data is local. Uninstalling the MSIX intentionally preserves user data. After a separate explicit data-deletion confirmation, remove:

- The exact `%LOCALAPPDATA%\CareerSeeker` workspace for an installed Beta.
- The configured source/test workspace when you ran from the repository.
- Exported documents you intentionally saved elsewhere.
- Any warmed local build caches you intentionally created for testing.

Resolve and inspect the exact target before recursive deletion. Do not combine app uninstall and user-data deletion.

### Verify the Audit Log

The Store implements hash-chain verification and the offline harnesses exercise it. From source or an
advanced terminal, use `export-audit` for hash-only JSON, `export-alpha-package` for a local evidence ZIP,
and `import-alpha-package` for safe restore into an isolated import workspace. Raw audit payloads are opt-in.

### Report a Fabrication Gate Issue

If you believe the Fabrication Gate incorrectly blocked a legitimate claim or allowed an unsupported one:

1. Record the posting ID or harness scenario.
2. Record the claim text and whether it was blocked or passed.
3. Email support@careerseeker.app with those details.

Fabrication Gate accuracy is a top-priority safety concern. Reports are triaged within 24 hours.

### Report a Draft Quality Issue

If a generated cover letter, tailored resume, or prepared answer contains errors, awkward phrasing, or misrepresented information:

1. Do not send the draft.
2. Record the posting ID, generated text, and a short description of the issue.
3. Email support@careerseeker.app.

CareerSeeker L1 contains no email-send implementation; you are always the final reviewer.

## Closed Beta

During closed beta:

- Support is provided via email only.
- Beta users may encounter rough edges in the UI, scoring, or draft quality.
- Fabrication Gate, draft quality, OAuth, and local-data deletion reports are high priority.
- Beta users can request help with full local data deletion and account disconnection at any time.

## Public Launch

At public launch, support channels and response commitments will be updated to reflect the production support model. This document will be revised accordingly.

*CareerSeeker is local-first. Windows-engine career data stays on your machine. Support exists to help you
control it, not to access it.*
