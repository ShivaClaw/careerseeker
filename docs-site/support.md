# CareerSeeker Support

**Last updated:** 2026-07-18

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

1. Open your [Google Account permissions page](https://myaccount.google.com/permissions).
2. Find CareerSeeker.
3. Remove access.
4. Delete the local DPAPI token vault configured for the alpha run, commonly `.appdata/oauth/gmail-token.dpapi`.

After disconnection, CareerSeeker cannot create Gmail drafts until you reconnect and authorize again.

An in-app "Disconnect Gmail" control that calls Google's revocation endpoint is planned for the product shell.

### Revoke LLM Provider Keys

Delete provider keys from the local vault, environment variable, or configuration location where you supplied them. CareerSeeker does not retain copies of provider keys outside the local configuration you control.

An in-app provider-key manager is planned for the product shell.

### Delete Local Data

Current alpha data is local. Depending on how you launched the app or harness, remove:

- The configured SQLite database, commonly `.appdata/careerseeker-alpha.db`.
- Generated artifacts or exported documents you created during testing.
- Local token vaults under `.appdata/`.
- Any warmed local build caches you intentionally created for testing.

Future installer builds will use a documented product data directory and uninstall flow.

### Verify the Audit Log

The Store implements hash-chain verification and the offline harnesses exercise it. Product UI for audit export is planned; until then, use the local store/harness code when validating alpha data.

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
