# CareerSeeker Autonomy Contract

**Version:** L1 Drafts alpha (v0.1)
**Last updated:** 2026-07-20

This contract defines what CareerSeeker can and cannot do on your behalf in the current L1 Drafts alpha. Every material action is intended to be logged in a local audit trail and reviewable by the user.

## L1 Drafts Mode

In L1 Drafts mode, CareerSeeker may:

| Action | Permitted | Details |
| --- | --- | --- |
| Discover job postings | Yes | Scans public ATS feeds for postings matching configured targets. |
| Score postings | Yes | Evaluates fit, legitimacy, and red flags using local profile/preferences. |
| Research employers | Yes | Builds company dossiers from public sources; ungrounded facts are dropped. |
| Tailor application materials | Yes | Generates cover letters and resume variants from source-of-truth profile claims. |
| Run the Fabrication Gate | Yes | Decomposes generated text into claims and verifies each against the profile. |
| Create Gmail drafts | Yes | Creates reviewable Gmail drafts after the Gate passes. |
| Create Gmail labels | No by default | Label management is split out of the L1 draft port because it requires broader Gmail permissions. |
| Send email | No | The L1 application has no send implementation and the Gmail draft port exposes no send method. |
| Submit applications | No | CareerSeeker does not interact with ATS submission portals in L1. |
| Answer recruiter questions | No | Novel free text is never auto-sent. |
| Book calendar events | No | No calendar access is requested in L1. |

You review. You send. CareerSeeker prepares.

## Non-Negotiable Safety Rules

These rules are enforced in code and covered by offline harnesses.

### 1. Fabrication Gate

Every tailored document is decomposed into atomic claims such as employer, title, dates, metrics, skills, and credentials. Each claim is verified against the source-of-truth profile. If any claim is unsupported, the application is blocked before draft creation.

The Gate cannot be bypassed by configuration. An application blocked by the Gate stays blocked until the underlying claim is either verified in the profile or removed from the generated text.

### 2. Pinned Verification Provider

The LLM provider route used for claim verification is pinned to the strong-provider tier. It is not budget-throttled, downgraded to a local model, or skipped. If verification infrastructure is unavailable, the application is deferred as `GATE_UNAVAILABLE` and no Gmail draft is created.

### 3. No Send Path in L1

The L1 Gmail draft interface exposes `CreateDraftAsync` only. There is no `SendAsync`, `SendMailAsync`, or public method with "send" in its name in the Dispatcher assembly.

The Gmail OAuth scope requested by L1 is `gmail.compose`. That Google permission can authorize sends, so the safety claim is deliberately not "the token cannot send." The safety claim is that the L1 application contains no send implementation.

### 4. Label Management Is Separate

Custom Gmail labels are not part of the default L1 draft capability. The L1 dispatcher can accept a separate `IGmailLabelManager` only when a build intentionally wires broader Gmail permissions.

### 5. Grounded Research Only

Company dossier facts must be traceable to retrieved source documents. Facts that cannot be grounded are dropped, not approximated. Hiring signals are positive-only and deterministic.

### 6. Hook Safety

Cover-letter hooks are scanned for candidate-claim patterns. A hook that could be read as a factual claim about the candidate is omitted.

### 7. Tamper-Evident Audit Log

State transitions, gate decisions, scoring events, and engine actions are recorded in a hash-chained audit log. Each entry's integrity depends on the previous entry.

## Current Controls

| Control | Current L1 alpha path |
| --- | --- |
| Pause or stop | Stop the local engine process or host. Product tray controls are planned. |
| Disconnect Gmail | Use `disconnect-gmail` or the packaged disconnect helper, then optionally confirm removal from Google Account permissions. |
| Edit claims | Edit the local source profile/claims and rerun affected work through the Gate. |
| Blocklist employers | Configure local preferences/rails when the product shell is wired. |
| Set daily cap | Planned product control; current harnesses exercise bounded local runs. |
| Export audit log or local evidence bundle | Available through `Advanced Tools/Export-CareerSeeker-Audit.cmd`, `Advanced Tools/Export-CareerSeeker-Evidence.cmd`, `Advanced Tools/Import-CareerSeeker-Package.cmd`, and the local `export-audit`, `export-alpha-package`, and `import-alpha-package` commands; audit-chain verification is implemented in the store and harnesses. |
| Delete all data | Delete local databases, generated artifacts, and `.appdata` test vaults. |

## Future Autonomy Levels

L2 and L3 modes are not available in v0.1. Before any higher autonomy level is activated, it must be explicitly designed, scoped, documented, and tested. Higher autonomy may require new Gmail, inbox, calendar, relay, or browser-automation permissions.

No higher autonomy level is activated by default.

## The Promise

CareerSeeker increases application throughput without compromising agency, truthfulness, account safety, or privacy. Every draft is yours to review. The signature is always yours.

*This contract is versioned with the product. Changes to autonomy levels, permitted actions, or safety rules should be documented before they take effect.*
