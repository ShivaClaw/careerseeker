# CareerSeeker Autonomy Contract

**Version:** L1 Drafts (v0.1)  
**Last updated:** [DATE]

This contract defines what CareerSeeker can and cannot do on your behalf. Every action CareerSeeker takes is logged in a tamper-evident audit trail and is reviewable at any time.

---

## L1 Drafts Mode — What CareerSeeker May Do

In L1 Drafts mode, CareerSeeker operates with the following permissions:

| Action | Permitted | Details |
|--------|-----------|---------|
| Discover job postings | ✅ | Scans public ATS feeds (Greenhouse, Lever, Ashby, and others) for postings matching your targets |
| Score postings | ✅ | Evaluates fit, legitimacy, and red flags using your profile and preferences |
| Research employers | ✅ | Builds a company dossier from public sources; facts are grounded-or-dropped, signals are positive-only |
| Tailor application materials | ✅ | Generates cover letters and resume variants using only your verified claims |
| Run the Fabrication Gate | ✅ | Decomposes every generated claim, verifies each against your source-of-truth profile, and blocks any unsupported claim |
| Create Gmail drafts | ✅ | Places completed, gate-cleared application materials into your Gmail Drafts folder for your review |
| **Send email** | ❌ | **The L1 Gmail interface has no send method. This is structural — the capability does not exist in the code.** |
| Submit applications | ❌ | CareerSeeker does not interact with any ATS submission portal |
| Answer recruiter questions | ❌ | Novel free text in your voice is never auto-sent |
| Book calendar events | ❌ | No calendar access is requested in L1 |

**You review. You send. CareerSeeker prepares.**

---

## Non-Negotiable Safety Rules

These rules are enforced in code and tested continuously. They cannot be disabled by configuration, preference, or any user setting.

### 1. Fabrication Gate

Every tailored document is decomposed into atomic claims (employer, title, dates, metrics, skills, credentials). Each claim is verified against your source-of-truth profile. If any claim is unsupported, the application is **blocked** — it cannot reach READY status.

The Gate cannot be bypassed. There is no override. There is no "send anyway." An application blocked by the Gate stays blocked until the underlying claim is either verified in your profile or removed from the generated text.

**Why this matters:** An autonomous career tool that fabricates credentials is a defamation machine pointed at its own user. A hiring manager who catches one fabricated certification doesn't just reject the application — in regulated industries, it can be disqualifying or unlawful.

### 2. Pinned Verification Provider

The LLM provider used for claim verification (the Fabrication Gate's inference call) is **never throttled, never downgraded, and fails closed** regardless of budget, rate limits, or provider availability. If the verification provider is unavailable, the application blocks. It does not fall through to a weaker model or skip verification.

### 3. No Send Path in L1

The L1 Gmail interface exposes `CreateDraftAsync` only. There is no `SendAsync` method, no `SendMailAsync` method, no method with "send" in its name. The Gmail OAuth scope requested is `gmail.compose` — the minimum scope for draft creation. This is not a policy; it is a structural constraint.

### 4. Grounded Research Only

Company dossier facts must be traceable to a retrieved source document. Facts that cannot be grounded are dropped, not approximated. Hiring signals are positive-only (e.g., "recently funded," "growing headcount") and deterministic — the Researcher does not speculate or editorialize.

### 5. Hook Safety

Cover letter hooks (attention-getting opening lines) are scanned for candidate-claim patterns. Any hook that could be read as a factual claim about you is omitted. Hooks are stylistic, never substantive.

### 6. Tamper-Evident Audit Log

Every state transition, gate decision, scoring event, and engine action is recorded in a hash-chained audit log. Each entry's integrity depends on the hash of the previous entry. Silent tampering with any entry breaks the chain and is detectable.

---

## Your Controls

You can exercise any of these controls at any time:

| Control | Effect |
|---------|--------|
| **Pause** | Engine stops processing; resume when ready |
| **Stop / Kill** | Immediate engine halt; acknowledged within seconds |
| **Disconnect Gmail** | Revokes OAuth token; calls Google's revocation endpoint |
| **Edit claims** | Add, modify, or delete any verified claim; changes re-trigger the Fabrication Gate on in-flight items |
| **Blocklist employers** | Prevent specific companies or domains from appearing in results |
| **Set score threshold** | Only postings above your minimum score generate application materials |
| **Set daily cap** | Maximum applications prepared per day |
| **Delete learned preferences** | Remove individual lessons or all campaign-learned patterns |
| **Export audit log** | Portable copy of the full hash-chained event log |
| **Delete all data** | Remove `%ProgramData%\CareerSeeker\` to delete everything |

---

## Future Autonomy Levels

L2 and L3 modes (auto-send, recruiter response, calendar scheduling) are **not available in v0.1**. Before any higher autonomy level is activated, you will be required to explicitly configure:

- Maximum applications per day
- Minimum score threshold for auto-send
- Current-employer and domain blocklist
- Approved Answer Bank for recruiter Q&A
- Quiet hours (no actions outside these windows)
- Calendar windows for interview scheduling (if enabled)

Each escalation requires your explicit opt-in. No autonomy level is activated by default.

---

## The Promise

CareerSeeker increases your application throughput without compromising your agency, your truthfulness, your account safety, or your privacy. Every material action is logged. Every draft is yours to review. Every control is yours to exercise.

**You are the applicant. CareerSeeker is the prep team. The signature is always yours.**

---

*This contract is versioned with the product. Changes to autonomy levels, permitted actions, or safety rules will be documented here before they take effect.*
