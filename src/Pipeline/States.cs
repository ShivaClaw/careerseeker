namespace SeekerSvc.Pipeline;

/// <summary>
/// The application lifecycle states (spec section 3.3). The enum member names are exactly the
/// strings the Store's <c>applications.state</c> CHECK constraint accepts, so
/// <c>state.ToString()</c> is the persisted value — one vocabulary, no translation table to drift.
/// </summary>
public enum AppState
{
    DISCOVERED,
    SCREENED,
    EVALUATED,
    REJECTED_BY_ENGINE,
    TAILORED,
    VERIFIED,
    BLOCKED_FABRICATION,
    GATE_UNAVAILABLE,
    READY,
    DRAFTED,
    GATE_PENDING,
    APPROVED,
    SUBMITTING,
    APPLIED,
    SKIPPED,
    GATE_EXPIRED,
    AWAITING_RESPONSE,
    RECRUITER_REPLY,
    CORRESPONDENCE,
    FOLLOWUP_DUE,
    FOLLOWUP_SENT,
    INTERVIEW_PROPOSED,
    SLOTS_OFFERED,
    SCHEDULED,
    REJECTED,
    OFFER,
    GHOSTED,
    USER_KILLED,
    PAUSED,
}

/// <summary>Autonomy level chosen at onboarding (spec section 2.1). Matches the schema vocabulary.</summary>
public enum AutonomyLevel { L1, L2, L3 }
