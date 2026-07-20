using SeekerSvc.Pipeline;
using SeekerSvc.Verifier;

namespace SeekerSvc.Tailor;

/// <summary>
/// A factual claim the generation model declares it made, with optional structured fields. The
/// Decomposer normalizes these and — critically — does NOT trust them to be complete: it also scans
/// the rendered prose for undeclared quantified/credential assertions.
/// </summary>
public sealed record DeclaredClaim(
    ClaimKind Kind,
    string Text,
    double? Number = null,
    string? Unit = null,
    double? DurationYears = null);

/// <summary>What the generation model returns: rendered text, the claims it declares, and answers.</summary>
public sealed record TailorDraft(
    string ResumeText,
    string CoverText,
    IReadOnlyList<DeclaredClaim> DeclaredClaims,
    IReadOnlyDictionary<string, string> Answers);

/// <summary>
/// The user's voice/style constraints (spec section 4.2, Bank 4). Banned phrases are enforced
/// deterministically; the cover-letter cap is the spec's 250 words.
/// </summary>
public sealed record StyleCard(IReadOnlyList<string> BannedPhrases, int MaxCoverWords = 250)
{
    public static StyleCard Default { get; } = new(
        new[] { "i am thrilled", "passionate about", "synergy", "think outside the box", "rockstar", "ninja" },
        250);
}

/// <summary>An exact, user-approved answer to a common application/recruiter question (spec section 4.4).</summary>
public sealed record ApprovedAnswer(string Question, string Answer);

/// <summary>Where an answer came from.</summary>
public enum AnswerSource
{
    /// <summary>Verbatim from the user's Approved Answer Bank — safe to auto-fill.</summary>
    ApprovedBank,

    /// <summary>No confident bank match — flagged for human review, never auto-answered.</summary>
    FlaggedNovel,
}

/// <summary>The resolution of one question against the Approved Answer Bank.</summary>
public sealed record AnswerResolution(string Question, string? Answer, AnswerSource Source)
{
    public bool IsAutoAnswerable => Source == AnswerSource.ApprovedBank && Answer is not null;
}

/// <summary>
/// What the generation model is given. Constraints carry prior gate violations on a rework.
/// <see cref="CompanyHook"/> is one grounded, employer-context line from the Researcher's dossier (spec
/// §5.5). It is optional and, when present, has already passed <see cref="HookGuard"/>. In the current L1
/// Gate, this is prompt context only: generated resume/cover prose is still verified only against candidate
/// profile facts, so the model is told not to quote or paraphrase employer-context facts into the draft.
/// </summary>
public sealed record TailorModelRequest(
    PipelineJob Job,
    IReadOnlyList<SourceClaim> Profile,
    IReadOnlyList<string> Constraints,
    StyleCard Style,
    IReadOnlyList<string> Questions,
    string? CompanyHook = null);

/// <summary>
/// Supplies the one grounded cover-letter hook for a job (spec §5.5). Backed by the Researcher's dossier
/// in production via a bridge; the Tailor depends only on this port, so it never references the Researcher.
/// Returns null when research grounded no usable hook — then the Tailor simply omits it.
/// </summary>
public interface IHookProvider
{
    Task<string?> GetHookAsync(PipelineJob job, CancellationToken ct = default);
}

/// <summary>
/// The generation port — where the LLM writes (spec section 5.6 routes this to the best tier). The
/// Tailor depends only on this contract; the real client lives behind the LLM Gateway.
/// </summary>
public interface ITailorModel
{
    Task<TailorDraft> GenerateAsync(TailorModelRequest request, CancellationToken ct = default);
}
