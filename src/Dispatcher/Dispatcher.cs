using SeekerSvc.Pipeline;

namespace SeekerSvc.Dispatcher;

/// <summary>
/// The L1 Dispatcher (spec §5.7). Implements the Pipeline's <see cref="IDispatcher"/>: it takes a
/// Gate-cleared <see cref="TailoredApplication"/> and leaves a ready-to-send draft in the user's Gmail
/// (lifecycle READY → DRAFTED). It renders the documents, packages them faithfully, builds the RFC 5322
/// message, and calls <see cref="IGmailDraftClient"/> with <c>gmail.compose</c> scope only.
///
/// Two structural safety facts:
///   • <b>Nothing here is reachable before the Gate.</b> The Pipeline graph makes the action state that
///     calls this dispatcher reachable only via VERIFIED; the Dispatcher adds no claim content, so it
///     cannot reintroduce risk the Gate already cleared.
///   • <b>An L1 Dispatcher cannot send.</b> <see cref="SubmitAsync"/> (L2/L3 submit) throws here; the
///     Gmail port exposes no send. Drafting and sending are different capabilities behind different
///     scopes, and this build holds only the draft capability.
/// </summary>
public sealed class Dispatcher : IDispatcher
{
    private readonly IPostingSource _postings;
    private readonly IDocumentRenderer _renderer;
    private readonly IGmailDraftClient _gmail;
    private readonly DispatcherConfig _config;

    public Dispatcher(
        IPostingSource postings,
        IDocumentRenderer renderer,
        IGmailDraftClient gmail,
        DispatcherConfig config)
    {
        _postings = postings;
        _renderer = renderer;
        _gmail = gmail;
        _config = config;
    }

    public async Task<DispatchOutcome> CreateDraftAsync(
        PipelineJob job, TailoredApplication app, CancellationToken ct = default)
    {
        var info = await _postings.GetDispatchInfoAsync(job.JobId, ct).ConfigureAwait(false);

        var resume = await _renderer.RenderResumeAsync(job, app, ct).ConfigureAwait(false);
        var coverPdf = _config.AttachCoverPdf
            ? await _renderer.RenderCoverAsync(job, app, ct).ConfigureAwait(false)
            : null;

        var pkg = PackageBuilder.Build(job, app, info, _config, resume, coverPdf);

        var labelIds = Array.Empty<string>();
        if (_config.UseCustomLabels)
        {
            var labelPath = pkg.Channel == DispatchChannel.Email ? _config.OutboxLabel : _config.ActionNeededLabel;
            var labelId = await _gmail.EnsureLabelAsync(labelPath, ct).ConfigureAwait(false);
            labelIds = new[] { labelId };
        }

        var raw = MimeBuilder.BuildRaw(
            _config.CandidateName, _config.CandidateEmail,
            pkg.Recipient ?? _config.CandidateEmail,
            pkg.Subject, pkg.BodyText, pkg.Attachments);

        var draftId = await _gmail.CreateDraftAsync(raw, labelIds, ct).ConfigureAwait(false);
        return new DispatchOutcome(Ok: true, pkg.Channel, Reference: draftId);
    }

    /// <summary>L2/L3 submit is not part of an L1 build. Sending is a separate, gated capability.</summary>
    public Task<DispatchOutcome> SubmitAsync(
        PipelineJob job, TailoredApplication app, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "L1 Dispatcher drafts only (gmail.compose). Submission/sending is an L2/L3 capability behind a separate gated port.");
}
