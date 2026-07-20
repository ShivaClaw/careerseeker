# CareerSeeker Support

**Last updated:** 2026-07-20

## Contact

| Channel | Address | Response target |
| --- | --- | --- |
| **General support** | support@careerseeker.app | 48 hours on business days |
| **Privacy inquiries** | privacy@careerseeker.app | 48 hours on business days |
| **Security reports** | security@careerseeker.app | 24 hours |
| **Product website** | https://careerseeker.app | Public product site |

During closed alpha and beta, support is provided by the development team directly. Response times may be faster than stated targets.

## Current Alpha Actions

### Disconnect Gmail

CareerSeeker L1 creates Gmail drafts through a local OAuth token vault. To disconnect the current alpha:

1. In the release package, double-click `Disconnect-CareerSeeker-Gmail.cmd`.
2. From source or a terminal, run `SeekerSvc.Engine.exe disconnect-gmail --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi`.
3. Optionally confirm removal on your [Google Account permissions page](https://myaccount.google.com/permissions).

After disconnection, CareerSeeker cannot create Gmail drafts until you reconnect and authorize again.

### Revoke LLM Provider Keys

In the release package, double-click `Clear-CareerSeeker-Providers.cmd` to delete the local DPAPI provider-key vault. From source or a terminal, run `SeekerSvc.Engine.exe clear-byok --key-vault .appdata/secrets/byok-keys.dpapi`.

Also delete any provider keys from environment variables or `secrets/env.secrets` if you supplied them there. CareerSeeker does not retain copies of provider keys outside the local configuration you control.

An in-app provider-key manager is planned for the product shell.

### Delete Local Data

Current alpha data is local. Depending on how you launched the app or harness, remove:

- The configured SQLite database, commonly `.appdata/careerseeker-alpha.db`.
- Generated artifacts or exported documents you created during testing.
- Local token vaults under `.appdata/`.
- Any warmed local build caches you intentionally created for testing.

Future installer builds will use a documented product data directory and uninstall flow.

### Verify the Audit Log

The Store implements hash-chain verification and the offline harnesses exercise it. In the release package:

- Double-click `Export-CareerSeeker-Audit.cmd` for hash-only audit JSON.
- Double-click `Export-CareerSeeker-Evidence.cmd` for a local ZIP evidence bundle.
- Double-click `Import-CareerSeeker-Package.cmd` for safe local restore into an import workspace.

From source or a terminal, the same paths are available through `export-audit`, `export-alpha-package`, and
`import-alpha-package`.

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

CareerSeeker L1 never sends automatically; you are always the final reviewer.

## Closed Beta

During closed beta:

- Support is provided via email only.
- Beta users may encounter rough edges in the UI, scoring, or draft quality.
- Fabrication Gate, draft quality, OAuth, and local-data deletion reports are high priority.
- Beta users can request help with full local data deletion and account disconnection at any time.

## Public Launch

At public launch, support channels and response commitments will be updated to reflect the production support model. This document will be revised accordingly.

*CareerSeeker is local-first. Your data stays on your machine. Support exists to help you control it, not to access it.*
