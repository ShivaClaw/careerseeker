namespace SeekerSvc.Verifier;

/// <summary>
/// Truth-strength of a source fact, assigned during onboarding (spec section 4.2,
/// Bank 3). A WEAK fact may appear and may be restated, but must never be amplified.
/// </summary>
public enum Confidence
{
    /// <summary>Corroborated by an uploaded artifact (resume, etc.).</summary>
    Verified,

    /// <summary>User asserted it; uncorroborated but allowed at face value.</summary>
    Stated,

    /// <summary>Coursework / exposure / aspirational. May appear, never amplified.</summary>
    Weak,
}

/// <summary>The kind of an atomic claim.</summary>
public enum ClaimKind
{
    Employer,
    Title,
    EmploymentDates,

    /// <summary>A quantified outcome, e.g. "reduced latency 30%".</summary>
    Metric,

    Skill,

    /// <summary>Cert / license. Strict: never inferred or fuzzed.</summary>
    Credential,

    Education,
    Other,
}

/// <summary>
/// An atomic fact from the Source-of-Truth Profile. The Gate's oracle.
/// The optional structured fields let the Gate do exact arithmetic (tenure) and
/// numeric checks instead of fuzzy text matching wherever the parser extracted them.
/// </summary>
public record SourceClaim(
    string Id,
    ClaimKind Kind,
    string Text,
    Confidence Confidence,
    string SourceDoc = "",
    double? Number = null,
    string? Unit = null,
    string? Employer = null,
    int? YearStart = null,
    int? YearEnd = null);   // YearEnd null == "present"

/// <summary>
/// An atomic claim extracted from generated resume / cover-letter / answer text.
/// IMPORTANT: claims handed to the Gate must already be atomic. Decomposing a
/// composite sentence into separate title / employer / tenure claims is the
/// Decomposer's job (spec section 5.5 step 1), not the Gate's. The Gate verifies
/// atoms; it does not parse prose.
/// </summary>
public record TailoredClaim(
    ClaimKind Kind,
    string Text,
    string Span = "",            // exact phrase in the document (for the diff view)
    double? Number = null,
    string? Unit = null,
    double? DurationYears = null);   // set if the claim asserts a tenure

/// <summary>The named reason a tailored claim failed.</summary>
public enum ViolationKind
{
    NoSupportingClaim,
    NumericMismatch,
    CredentialNotFound,
    DateMismatch,
    QuantifiedWeakClaim,
    UpgradedConfidence,
}

/// <summary>
/// A single reason a tailored claim failed. Carries enough to render the spec's
/// diff view: "I wanted to say X; your profile only supports Y."
/// </summary>
public record Violation(
    TailoredClaim Claim,
    ViolationKind Kind,
    string Explanation,
    string? NearestSource = null);   // the closest thing the profile DOES support

public enum Verdict
{
    Ready,
    BlockedFabrication,
}

public record VerificationResult(
    Verdict Verdict,
    IReadOnlyList<Violation> Violations,
    int ClaimsChecked)
{
    public bool Passed => Verdict == Verdict.Ready;
}
