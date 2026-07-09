# CareerSeeker Support

## Contact

| Channel | Address | Response target |
|---------|---------|-----------------|
| **General support** | support@careerseeker.app | 48 hours (business days) |
| **Privacy inquiries** | privacy@careerseeker.app | 48 hours (business days) |
| **Security reports** | security@careerseeker.app | 24 hours |
| **Product website** | https://careerseeker.app | — |

During closed beta, support is provided by the development team directly. Response times may be faster than stated targets.

---

## Common Actions

### Disconnect Gmail

1. Open CareerSeeker from the system tray → **Settings** → **Connected Accounts**.
2. Click **Disconnect Gmail**.
3. CareerSeeker calls Google's token revocation endpoint and deletes the local refresh token from the DPAPI vault.
4. You can also revoke access from your [Google Account permissions page](https://myaccount.google.com/permissions) → find "CareerSeeker" → **Remove Access**.

After disconnection, CareerSeeker cannot create Gmail drafts until you reconnect.

### Revoke LLM Provider Keys

1. Open **Settings** → **Provider Keys**.
2. Select the provider to remove → **Delete Key**.
3. The key is removed from the local DPAPI-encrypted vault immediately.

CareerSeeker does not retain copies of your API keys outside the local vault.

### Delete Local Data

All CareerSeeker data is stored on your machine at `%ProgramData%\CareerSeeker\`.

- **Uninstall** the application via Windows Settings → Apps to remove the engine and tray application.
- **Delete the data directory** (`%ProgramData%\CareerSeeker\`) to remove the database, generated documents, audit log, and encrypted backups.
- These two steps remove all CareerSeeker data from your machine.

### Export Audit Log

1. Open **Settings** → **Audit & History** → **Export Log**.
2. The export produces a portable file containing the full hash-chained event log.
3. Each entry includes a hash of the previous entry — you can independently verify the chain's integrity.

### Report a Fabrication Gate Issue

If you believe the Fabrication Gate incorrectly blocked a legitimate claim or allowed an unsupported one:

1. Open the application's audit log for the specific posting.
2. Note the posting ID and the claim text in question.
3. Email support@careerseeker.app with the posting ID, the claim, and whether it was blocked or passed.

Fabrication Gate accuracy is a top-priority safety concern. Reports are triaged within 24 hours.

### Report a Draft Quality Issue

If a generated cover letter, tailored resume, or prepared answer contains errors, awkward phrasing, or misrepresented information:

1. Do **not** send the draft — CareerSeeker L1 never sends automatically; you are always the final reviewer.
2. Email support@careerseeker.app with the posting ID, the generated text, and a description of the issue.

---

## Closed Beta

During closed beta (up to 100 test users under Google's OAuth test-user cap):

- Support is provided via email only.
- Beta users may encounter rough edges in the UI, scoring, or draft quality.
- All beta feedback — especially Fabrication Gate and draft quality reports — directly shapes the product before public launch.
- Beta users can request full data deletion and account disconnection at any time using the steps above.

## Public Launch

At public launch, support channels and response commitments will be updated to reflect the production support model. This document will be revised accordingly.

---

*CareerSeeker is local-first. Your data stays on your machine. Support exists to help you control it, not to access it.*
