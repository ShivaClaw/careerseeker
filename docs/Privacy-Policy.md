# CareerSeeker Privacy Policy

**Effective date:** [DATE]  
**Last updated:** [DATE]  
**Product version:** L1 Drafts (v0.1)

CareerSeeker is a local-first Windows application that helps job seekers discover, evaluate, and apply to positions. This policy describes what data the application processes, where it lives, and what controls you have.

---

## 1. Architecture Summary

CareerSeeker runs entirely on your Windows machine. The application pipeline, database, generated documents, OAuth tokens, and API keys never leave your device except in the specific circumstances described below.

There is no CareerSeeker account. There is no CareerSeeker cloud service that processes your resume, job data, or application materials.

For L1 Drafts mode, CareerSeeker requests `gmail.compose`. This permission can authorize Gmail sends, but the L1 app creates reviewable drafts only: it contains no send implementation and its Gmail interface exposes no send method. Custom Gmail labels are deferred in L1 because they require broader Gmail access.

## 2. Data We Process

All of the following data is created, stored, and processed locally on your machine:

| Category | Examples |
|----------|----------|
| **Profile & claims** | Resume text, verified claims, skills, credentials, work history, compensation preferences, role targets, employer blocklists, approved answer bank |
| **Job postings** | Titles, descriptions, companies, locations, and metadata discovered from public ATS feeds (Greenhouse, Lever, Ashby, and others) |
| **Generated materials** | Tailored resumes, cover letters, prepared interview answers, employer dossiers |
| **Scores & decisions** | Fit scores, legitimacy scores, red-flag assessments, Fabrication Gate verdicts |
| **Audit log** | Every state transition, gate decision, and engine action in a tamper-evident, hash-chained local log |
| **Credentials** | Google OAuth refresh tokens and LLM provider API keys, encrypted with Windows DPAPI scoped to the service account |

## 3. Gmail Access

CareerSeeker requests a single restricted Google OAuth scope: **`gmail.compose`** (create drafts only).

In L1 Drafts mode, CareerSeeker:

- **Creates** Gmail draft messages containing your tailored application materials
- **Does not** send email, read your inbox, modify existing messages, or access contacts

The L1 Gmail interface has no send method. This is enforced structurally in code — the send capability does not exist in the application, not merely disabled.

You can revoke Gmail access at any time from within CareerSeeker or from your [Google Account permissions page](https://myaccount.google.com/permissions). Revocation takes effect immediately.

## 4. Third-Party LLM Inference

CareerSeeker uses large language models for scoring, tailoring, research, and verification. In **BYOK (Bring Your Own Key) mode**, you supply your own API key for your chosen provider (e.g., Anthropic, Google). Inference requests travel directly from your machine to the provider you selected. CareerSeeker does not proxy, log, or retain your API key beyond the local DPAPI-encrypted vault.

Data sent to inference providers includes job posting text, your profile claims relevant to that posting, and generated draft text for verification. **Your full resume is never sent in a single request** — only the claims relevant to the specific posting being processed.

Each provider's own privacy policy governs their handling of API requests. CareerSeeker surfaces which provider is used for each pipeline stage and provides token/cost accounting in the local dashboard.

The Fabrication Gate — the verification stage that ensures no unsupported claims appear in your application materials — uses a **pinned provider tier** that is never throttled, downgraded, or skipped, regardless of budget or rate limits. This is a safety invariant, not a preference.

## 5. Planned Cloud Component (Not Yet Active)

A future release may include an optional Android companion app paired to your Windows engine via a blind relay service. The relay:

- Stores **only end-to-end encrypted event blobs** (keys exchanged locally via QR pairing)
- Cannot read resumes, job data, emails, application contents, or any plaintext
- Provides push notifications and offline catch-up only

The relay is not active in L1. When launched, pairing will be optional and revocable.

## 6. Data We Do Not Collect

CareerSeeker does not:

- Create user accounts on any CareerSeeker-operated server
- Transmit your resume, profile, job data, or application materials to CareerSeeker-operated infrastructure
- Use analytics, telemetry, advertising, or tracking services
- Share, sell, or license your data to third parties
- Retain data after you delete it locally

## 7. Your Controls

| Action | How |
|--------|-----|
| **Pause or stop** the engine | System tray, local dashboard, or mobile dashboard (when paired) |
| **Revoke Gmail access** | In-app "Disconnect Gmail" button (calls Google's token revocation endpoint) or Google Account permissions |
| **Revoke LLM provider keys** | Delete from the local vault via the settings panel |
| **Delete all local data** | Uninstall removes the application; `%ProgramData%\CareerSeeker\` contains the database and generated files — delete this directory to remove all data |
| **Export audit log** | Export command produces a portable copy of the full hash-chained event log |
| **Delete learned preferences** | Individual lessons and campaign-learned patterns are deletable individually or wholesale |
| **Remove verified claims** | Edit or delete any claim in your profile at any time; changes re-trigger the Fabrication Gate on in-flight items |

## 8. Data Retention

All data is stored locally with no expiration. You control retention by deleting data through the controls described above. Nightly encrypted local backups rotate on a 14-day cycle. No data is retained on any server operated by CareerSeeker.

## 9. Security

- OAuth tokens and API keys are encrypted with **Windows DPAPI** scoped to the service account — they are not stored in plaintext
- The audit log is **hash-chained** — each entry's integrity depends on the previous entry, making silent tampering detectable
- The application is **Authenticode-signed** with an EV code-signing certificate
- No secrets, tokens, or user data are committed to source control or transmitted in diagnostic output

## 10. Children's Privacy

CareerSeeker is not directed at individuals under 16. We do not knowingly process data from children.

## 11. Changes to This Policy

We will update this policy when the product's data practices change. The "Last updated" date at the top reflects the most recent revision. Material changes (new data collection, new cloud services, new OAuth scopes) will be communicated through the application's update notes.

## 12. Contact

- **Privacy inquiries:** privacy@careerseeker.app
- **General support:** support@careerseeker.app
- **Product website:** https://careerseeker.app

---

*CareerSeeker is built on the principle that your career data belongs on your machine, not on ours.*
