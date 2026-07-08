# CareerSeeker Privacy Policy Placeholder

Status: draft placeholder for OAuth verification prep. This is not final legal text.

CareerSeeker is designed as a local-first Windows engine. The application pipeline, SQLite store, OAuth refresh tokens, API keys, generated documents, and resume/profile data live on the user's machine.

## Data We Process Locally

- Resume, profile, claims, preferences, target roles, compensation preferences, blocklists, and approved answer bank.
- Job postings discovered from supported job sources.
- Generated application materials, including resume drafts, cover letters, prepared answers, dossiers, and audit events.
- OAuth tokens and provider API keys stored in the local Windows DPAPI-protected vault.

## Gmail Access

For L1 Drafts mode, CareerSeeker requests Gmail draft capability only. The app creates reviewable Gmail drafts and labels. It does not send email in L1 mode, and the L1 Gmail interface has no send method.

## Cloud Services

CareerSeeker does not run the application pipeline on CareerSeeker servers. The planned cloud component is a blind relay for the Android companion app. Relay payloads are end-to-end encrypted; the relay is not intended to read resumes, job data, emails, or application contents.

## Third-Party Inference

In BYOK mode, the user supplies their own inference provider key. Requests go from the local engine to the selected provider. CareerSeeker should disclose which provider is used for each stage and surface token/cost accounting.

## User Controls

Users can pause or stop the engine, revoke connected accounts, delete local data, export the audit log, and remove learned preferences. Public-launch wording must describe these controls in the product UI and final policy.

## Contact

Support contact is TBD. See `docs/Support.md`.
