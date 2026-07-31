# CareerSeeker Privacy Policy

**Effective date:** 2026-07-18
**Last updated:** 2026-07-30
**Product version:** L1 Drafts beta (v0.7)

CareerSeeker is a local-first Windows application that helps job seekers discover, evaluate, and prepare job applications. This policy describes what data the current L1 Drafts beta processes, where it lives, and what controls you have.

## 1. Architecture Summary

CareerSeeker runs on your Windows machine. The application pipeline, SQLite database, generated documents, OAuth tokens, and API keys are local, except when you choose to connect external services such as Gmail or LLM providers.

There is no CareerSeeker product account and no CareerSeeker-operated cloud service that processes your resume, job data, or application materials in the current Windows L1 beta. The public tester-signup service stores signup/release metadata, not Windows-engine profile or application content. A future Android relay is planned as an optional end-to-end encrypted component; it is not active in L1.

## 2. Data We Process

All of the following data is created, stored, and processed locally on your machine:

| Category | Examples |
| --- | --- |
| **Profile and claims** | Resume text, verified claims, skills, credentials, work history, compensation preferences, role targets, employer blocklists, approved answer bank |
| **Job postings** | Titles, descriptions, companies, locations, and metadata discovered from public ATS feeds |
| **Generated materials** | Tailored resumes, cover letters, prepared answers, employer dossiers |
| **Scores and decisions** | Fit scores, legitimacy scores, red-flag assessments, Fabrication Gate verdicts |
| **Audit log** | State transitions, gate decisions, and engine actions in a hash-chained local log |
| **Credentials** | Google OAuth refresh tokens and LLM provider API keys, encrypted with Windows DPAPI in the local user profile |

## 3. Gmail Access

CareerSeeker L1 requests the Google OAuth scope `gmail.compose`.

Google describes this scope as draft and send capable. CareerSeeker's L1 safeguard is therefore application-level and structural: the L1 code creates reviewable Gmail drafts only, the draft port exposes only `CreateDraftAsync`, and the application contains no send implementation.

In L1 Drafts mode, CareerSeeker:

- Creates Gmail draft messages containing gate-cleared application materials.
- Does not send email.
- Does not read your inbox.
- Does not modify existing messages.
- Does not access contacts.
- Does not create Gmail labels by default; label management is a separate capability because it requires broader Gmail permissions.

You can revoke Gmail access at any time from your [Google Account permissions page](https://myaccount.google.com/permissions). The current beta also stores token material in a local DPAPI vault path that can be deleted when disconnecting the authorized Gmail account.

## 4. Third-Party LLM Inference

CareerSeeker uses large language models for scoring support, tailoring, research, and verification. In BYOK mode, you supply your own API key for your chosen provider. Inference requests travel from your machine to that provider. CareerSeeker does not currently proxy, log, or retain your API key beyond the local DPAPI-encrypted vault.

Data sent to inference providers may include job posting text, company research snippets, source-of-truth profile claims, generated draft text, and Fabrication Gate verification prompts. Tailor generation filters profile claims to posting-relevant facts before sending a prompt. Fabrication Gate verification still uses the local source-of-truth profile as its oracle and may send bounded verification prompts for generated claims.

Each provider's own privacy policy governs its handling of API requests. CareerSeeker records local token/cost accounting for provider calls.

The Fabrication Gate uses a pinned strong-provider route for claim verification. That route is never budget-throttled or downgraded. If verification infrastructure is unavailable, the application is deferred as `GATE_UNAVAILABLE`; it is not marked ready and it is not mislabeled as a fabrication block.

## 5. Google API Limited Use

CareerSeeker's use of information received from Google APIs will adhere to the Google API Services User Data Policy, including the Limited Use requirements.

CareerSeeker does not use Google user data to train generalized AI or ML models. Gmail data is used only to create user-reviewable drafts requested by the local L1 workflow and to maintain the local connection authorized by the user.

## 6. Planned Cloud Component

A future release may include an optional Android companion app paired to your Windows engine through a blind relay service. The relay is intended to store only end-to-end encrypted event blobs, provide push notifications, and support offline catch-up. The relay must not read resumes, job data, emails, application contents, or other plaintext.

The relay is not active in L1. When launched, pairing will be optional and revocable.

## 7. Data We Do Not Collect

CareerSeeker does not:

- Create user accounts on any CareerSeeker-operated server in L1.
- Transmit your resume, profile, job data, or application materials to CareerSeeker-operated infrastructure in L1.
- Intentionally include analytics, advertising, or tracking in the Windows engine. A whole-product/deployed-site
  inventory is still pending and no broader audited claim is made.
- Share, sell, or license your data to third parties.
- Retain Windows-engine profile, job, application, or generated-material data on a CareerSeeker server. The
  separate tester-signup service retains its own signup/release metadata.

## 8. Your Controls

| Action | Current L1 beta path |
| --- | --- |
| **Pause or stop the engine** | Use the local dashboard/control files or stop the local process/service host. A native tray is not built. |
| **Revoke Gmail access** | Use the token-protected local dashboard control, or run `CareerSeeker.exe disconnect-gmail` from source/an unpacked test tree, then optionally confirm removal from Google Account permissions. |
| **Revoke LLM provider keys** | Run `CareerSeeker.exe clear-byok` from source/an unpacked test tree, then delete any environment or `secrets/env.secrets` copies you supplied. |
| **Delete all local data** | Uninstalling preserves user data. After a separate explicit confirmation, delete the exact `%LOCALAPPDATA%\CareerSeeker` workspace (or the configured source/test workspace). |
| **Export audit log or local evidence bundle** | Run the local `export-audit`, `export-alpha-package`, or `import-alpha-package` command from source/an unpacked test tree; raw event payloads are opt-in. Historical Alpha `.cmd` helpers are not part of the current MSIX. |
| **Remove verified claims** | Edit the local profile/claims source and rerun affected applications through the Gate. |

## 9. Data Retention

Windows-engine L1 profile, job, application, credential, and generated-material data is stored locally with no CareerSeeker server-side retention. The separate public tester-signup service has its own operational signup/release metadata. You control Windows-engine retention by deleting local databases, generated artifacts, and vault files.

Nightly encrypted local backups and in-app deletion controls are planned product features, not completed L1 beta features.

## 10. Security

- OAuth tokens and API keys are stored in a Windows DPAPI-protected local vault when using the provided OAuth/token helpers.
- The audit log is hash-chained; each entry's integrity depends on the previous entry.
- The current Beta MSIX is unsigned. Public release builds are expected to be signed; the signing hook is implemented but no production signature is claimed.
- No secrets, tokens, resumes, or user profile data should be committed to source control or transmitted in diagnostic output.

## 11. Children's Privacy

CareerSeeker is not directed at individuals under 16. We do not knowingly process data from children.

## 12. Changes to This Policy

We will update this policy when the product's data practices change. Material changes, including new cloud services or new OAuth scopes, should be documented before release.

## 13. Contact

- **Privacy inquiries:** privacy@careerseeker.app
- **General support:** support@careerseeker.app
- **Product website:** https://careerseeker.app

*CareerSeeker is built on the principle that Windows-engine career data belongs on your machine, not on ours.*
