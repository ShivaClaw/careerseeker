# CareerSeeker — Future Design Ideas

Concepts in this file are explicitly **not on the active roadmap**. They require no
implementation work, no scope decisions, and no test coverage until a deliberate
decision moves an item out of this file and into the `.mf`. Anything here may be
changed, merged, or dropped without touching the current build.

---

## CareerSeeker Cloud (future — monthly subscription, managed inference)

**What it is:** A metered, CareerSeeker-hosted inference subscription (~$9–15/mo),
covering the recurring-cost pipeline stages that a one-time payment cannot sustain —
principally **generative correspondence**: drafting follow-up replies and interview
confirmations on the user's behalf.

**Why it's parked here and not on the roadmap:**

- Local-first is a founding constraint, not a preference. Managed inference is a
  hosted pipeline for the single most sensitive stage (full resume + JD + generated
  personal correspondence) run through our infrastructure, for money — the textbook
  case the local-first principle exists to reject.
- Unbounded recurring cost funded by a subscription price is a real, ongoing margin
  exposure (every vendor price change compresses margin in real time), not a one-time
  engineering cost.
- Turns "ship a desktop tool" into "operate a metered SaaS handling personal
  correspondence" — a large increase in surface area (billing, abuse protection,
  server-side secrets, sub-processor privacy disclosures) for a pre-revenue,
  one-founder project already carrying CASA, restricted-scope OAuth, code signing,
  and LLC formation.
- Not validated: whether BYOK friction is actually a meaningful conversion cliff for
  this audience is an assumption, not data, as of this writing.

**If revisited, phased build (do not start without an explicit go-ahead):**

- **M0 — Validate first.** Run closed beta with BYOK-only onboarding; measure actual
  drop-off at "generate your own API key." This number is the entire business case.
- **M1 — Bounded starter credit.** Short-lived, capped CareerSeeker-issued key
  (first N applications or 14 days), zero content logging, hard per-user spend cap
  enforced server-side (not client-side accounting). Requires a live backend relay,
  abuse/rate-limit protection, and a Privacy Policy update disclosing CareerSeeker as
  an intermediary for this path.
- **M2 — Full metered subscription.** Real accounts beyond the paired install,
  recurring billing (Stripe or similar), dunning/refunds/regional tax, per-user usage
  metering wired to the existing budget-cap UI, defined lifecycle behavior for a
  lapsed payment mid-tailoring-cycle.
- **M3 — Product integration.** Plan management, graceful degrade/upgrade UX.

**Gmail/Calendar scope implication if built:** requires `gmail.send` + `gmail.modify`

+ `calendar.events` (per spec §4.3's incremental-scope model) — Google restricted-scope
  OAuth verification is a hard external dependency, separate from and beyond the
  compose-only CASA path Basic already needs.

**Naming:** "Cloud," not "Pro" — deliberately distinct so a one-time unlock (Pro) and
a recurring managed-inference service (Cloud) never collide in product copy.

---

## CareerSeeker Pro (future — one-time in-app unlock, no ongoing inference cost)

**What it is:** A one-time paid unlock (~$9.99) adding **reply tracking and
categorization** — surfacing where every dispatched application actually stands
(pending / no response / no interview / interview offered) — without generating any
new outbound content.

**Explicit scope boundary (do not let this drift):**

- **In scope:** detecting a reply exists, categorizing it into the four states above,
  surfacing that state in the dashboard/local UI.
- **Out of scope, by design:** drafting or sending any follow-up, confirmation, or
  correspondence text. Any generative correspondence — even a templated
  "confirming Tuesday 2pm works" reply — is Gate-mandatory content generation and
  belongs under Cloud, not Pro. This is the split we deliberately chose specifically
  to keep Pro's one-time price honest about not funding ongoing generation.

**Cost-model target:** categorization should run as on-device classification
(`CapabilityClass.OnDeviceSmall` / heuristic matching — reply-received signal,
keyword/pattern heuristics for rejection vs. interview-offer language) rather than a
per-reply cloud LLM call, so "no ongoing inference cost" is a real property of the
implementation, not just a billing decision.

**Gmail scope implication if built:** categorization still requires **reading** the
inbox to detect a reply arrived — this needs `gmail.readonly` (or `gmail.modify` if
labeling), which is scope beyond `gmail.compose` even though it's read-only and lower
risk than `gmail.send`. Still requires its own restricted-scope consideration; do not
assume "no send capability" means "no new scope."

**Consent/rails implication if built:** even though Pro adds no send capability,
turning on inbox-read access is itself an incremental Gmail scope grant and should go
through the same explicit, documented opt-in pattern as any other scope escalation —
not bundled silently into the purchase flow.

---

## Cross-cutting notes for both

- Neither concept is referenced by name anywhere in the active `.mf`,
  `CareerSeeker-Spec.md`, or the nine-step remediation sequence. If either moves onto
  the roadmap, update those documents explicitly at that time — don't let scope
  creep back in via inference.
- "CareerSeeker" (Windows engine) and "CareerSeeker Dashboard" ($4.99 apk) are
  unaffected by anything in this file and remain as currently specified.
