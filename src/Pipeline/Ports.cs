using SeekerSvc.Verifier;

namespace SeekerSvc.Pipeline;

/// <summary>The slice of a job the tailor and dispatcher need. Carried through the pipeline.</summary>
public sealed record PipelineJob(
    long JobId,
    string Title,
    string Company,
    string? ApplyUrl = null,
    string? DescriptionText = null);

/// <summary>
/// A tailored application produced by the Tailor. <see cref="Claims"/> are the atomic
/// claims the Fabrication Gate verifies against the profile; the rendered text is what gets drafted or
/// submitted once (and only once) the gate passes.
/// </summary>
public sealed record TailoredApplication(
    IReadOnlyList<TailoredClaim> Claims,
    string ResumeText,
    string CoverText,
    IReadOnlyDictionary<string, string> Answers);

/// <summary>How an application is delivered (matches the Store's application.channel values).</summary>
public enum DispatchChannel { AtsForm, Email, ManualFinish }

/// <summary>Result of a dispatch action.</summary>
public sealed record DispatchOutcome(
    bool Ok,
    DispatchChannel Channel,
    string? Reference = null,
    string? ResumePath = null,
    string? CoverPath = null,
    string? AnswersJson = null);

/// <summary>
/// Produces a tailored application for a job from the Source-of-Truth claims. The pipeline only depends on
/// this contract; Engine wires the concrete Tailor adapter for alpha runs. On a rework loop the prior gate
/// violations are passed back so the tailor can correct rather than repeat.
/// </summary>
public interface ITailor
{
    Task<TailoredApplication> TailorAsync(
        PipelineJob job,
        IReadOnlyList<SourceClaim> profile,
        IReadOnlyList<Violation> priorViolations,
        CancellationToken ct = default);
}

/// <summary>
/// Delivers an application after the Fabrication Gate passes. The L1 implementation creates Gmail drafts
/// only; submit/send automation belongs to a later gated capability and throws in the alpha dispatcher.
/// The pipeline keeps both verbs so higher autonomy levels can be designed explicitly instead of smuggled
/// through the draft-only port.
/// </summary>
public interface IDispatcher
{
    /// <summary>L1: leave a ready-to-send Gmail draft for the user to review and send.</summary>
    Task<DispatchOutcome> CreateDraftAsync(PipelineJob job, TailoredApplication app, CancellationToken ct = default);

    /// <summary>L2 (post-approval) / L3 (within rails): submit the application.</summary>
    Task<DispatchOutcome> SubmitAsync(PipelineJob job, TailoredApplication app, CancellationToken ct = default);
}
