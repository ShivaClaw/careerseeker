using SeekerSvc.Pipeline;
using SeekerSvc.Verifier;

namespace SeekerSvc.Tailor;

/// <summary>A richer tailoring outcome for observability (the port returns only the application).</summary>
public sealed record TailorResult(
    TailoredApplication Application,
    StyleReport Style,
    IReadOnlyList<string> FlaggedQuestions,
    IReadOnlyList<string> ConstraintsUsed);

/// <summary>
/// The Tailor (spec section 5.5). Implements the Pipeline's <see cref="ITailor"/>: it asks the
/// generation model for a draft, decomposes it into atomic claims for the Gate, resolves answers
/// against the Approved Answer Bank (novel questions are flagged, never invented), and checks the
/// cover letter against the style card. On a rework it turns the prior Gate violations into explicit
/// "do not claim X" constraints so the model corrects rather than repeats.
///
/// The Tailor does not pass or fail itself — the Pipeline runs the Gate on the claims this produces.
/// Its safety contribution is upstream of that: faithful decomposition, so the Gate sees the real
/// claims; and answer-bank discipline, so no novel statement is auto-filled in the user's voice.
/// </summary>
public sealed class Tailor : ITailor
{
    private readonly ITailorModel _model;
    private readonly StyleCard _style;
    private readonly IReadOnlyList<ApprovedAnswer> _answerBank;
    private readonly IReadOnlyList<string> _questions;
    private readonly IHookProvider? _hooks;

    public Tailor(
        ITailorModel model,
        StyleCard? style = null,
        IReadOnlyList<ApprovedAnswer>? answerBank = null,
        IReadOnlyList<string>? questions = null,
        IHookProvider? hooks = null)
    {
        _model = model;
        _style = style ?? StyleCard.Default;
        _answerBank = answerBank ?? Array.Empty<ApprovedAnswer>();
        _questions = questions ?? Array.Empty<string>();
        _hooks = hooks;
    }

    public async Task<TailoredApplication> TailorAsync(
        PipelineJob job, IReadOnlyList<SourceClaim> profile,
        IReadOnlyList<Violation> priorViolations, CancellationToken ct = default)
        => (await TailorDetailedAsync(job, profile, priorViolations, ct).ConfigureAwait(false)).Application;

    public async Task<TailorResult> TailorDetailedAsync(
        PipelineJob job, IReadOnlyList<SourceClaim> profile,
        IReadOnlyList<Violation> priorViolations, CancellationToken ct = default)
    {
        var constraints = ConstraintsFrom(priorViolations);

        // One grounded, employer-context hook (spec §5.5). HookGuard drops any hook carrying a number,
        // credential, or amplifier the Decomposer would read as a *candidate* claim — so a hook can never
        // become an unverifiable Gate atom. When unsafe or absent, the hook is simply omitted.
        var hook = _hooks is null ? null : await _hooks.GetHookAsync(job, ct).ConfigureAwait(false);
        var safeHook = HookGuard.IsSafe(hook) ? hook : null;

        var draft = await _model.GenerateAsync(
            new TailorModelRequest(job, profile, constraints, _style, _questions, safeHook), ct).ConfigureAwait(false);

        var claims = Decomposer.FromDraft(draft);
        var (answered, flagged) = AnswerBank.ResolveAll(_questions, _answerBank);
        var style = StyleGuard.Check(draft.CoverText, _style);

        var application = new TailoredApplication(claims, draft.ResumeText, draft.CoverText, answered);
        return new TailorResult(application, style, flagged, constraints);
    }

    /// <summary>Turn prior gate violations into explicit generation constraints for the rework pass.</summary>
    public static IReadOnlyList<string> ConstraintsFrom(IReadOnlyList<Violation> violations)
    {
        var list = new List<string>();
        foreach (var v in violations)
        {
            var nearest = string.IsNullOrWhiteSpace(v.NearestSource)
                ? "Do not include it at all."
                : $"The most you can say is what the profile supports: \"{v.NearestSource}\".";
            list.Add($"Do not claim \"{v.Claim.Text}\". {v.Explanation} {nearest}");
        }
        return list;
    }
}
