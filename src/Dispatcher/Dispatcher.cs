using System.Text.Json;
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
///   • <b>An L1 Dispatcher cannot send.</b> <see cref="SubmitAsync"/> (L2/L3 submit) throws here and the
///     Gmail port exposes no send method. The OAuth <c>gmail.compose</c> permission can authorize Gmail
///     sends, so this is an application-level structural safeguard, not a claim about the token's power.
/// </summary>
public sealed class Dispatcher : IDispatcher
{
    private readonly IPostingSource _postings;
    private readonly IDocumentRenderer _renderer;
    private readonly IGmailDraftClient _gmail;
    private readonly IGmailLabelManager? _labels;
    private readonly DispatcherConfig _config;

    public Dispatcher(
        IPostingSource postings,
        IDocumentRenderer renderer,
        IGmailDraftClient gmail,
        DispatcherConfig config,
        IGmailLabelManager? labels = null)
    {
        _postings = postings;
        _renderer = renderer;
        _gmail = gmail;
        _labels = labels;
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
            if (_labels is null)
                throw new InvalidOperationException(
                    "Custom Gmail labels require an IGmailLabelManager capability; the L1 draft client is compose-only.");

            var labelPath = pkg.Channel == DispatchChannel.Email ? _config.OutboxLabel : _config.ActionNeededLabel;
            var labelId = await _labels.EnsureLabelAsync(labelPath, ct).ConfigureAwait(false);
            labelIds = new[] { labelId };
        }

        var raw = MimeBuilder.BuildRaw(
            _config.CandidateName, _config.CandidateEmail,
            pkg.Recipient ?? _config.CandidateEmail,
            pkg.Subject, pkg.BodyText, pkg.Attachments);

        var draftId = await _gmail.CreateDraftAsync(raw, labelIds, ct).ConfigureAwait(false);
        var artifacts = await TrySaveArtifactsAsync(job, app, draftId, resume, coverPdf, ct).ConfigureAwait(false);
        return new DispatchOutcome(
            Ok: true,
            pkg.Channel,
            Reference: draftId,
            ResumePath: artifacts.ResumePath,
            CoverPath: artifacts.CoverPath,
            AnswersJson: artifacts.AnswersJson);
    }

    /// <summary>L2/L3 submit is not part of an L1 build. Sending is a separate, gated capability.</summary>
    public Task<DispatchOutcome> SubmitAsync(
        PipelineJob job, TailoredApplication app, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "L1 Dispatcher drafts only (gmail.compose). Submission/sending is an L2/L3 capability behind a separate gated port.");

    private async Task<SavedArtifacts> TrySaveArtifactsAsync(
        PipelineJob job,
        TailoredApplication app,
        string draftId,
        Attachment resume,
        Attachment? cover,
        CancellationToken ct)
    {
        var artifactDirectory = _config.ArtifactDirectory;
        if (string.IsNullOrWhiteSpace(artifactDirectory))
            return new SavedArtifacts(null, null, AnswersJson(app));

        try
        {
            return await SaveArtifactsAsync(job, app, draftId, resume, cover, artifactDirectory, ct)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new SavedArtifacts(null, null, AnswersJson(app));
        }
    }

    private async Task<SavedArtifacts> SaveArtifactsAsync(
        PipelineJob job,
        TailoredApplication app,
        string draftId,
        Attachment resume,
        Attachment? cover,
        string artifactDirectory,
        CancellationToken ct)
    {

        var artifactRoot = Path.GetFullPath(artifactDirectory);
        var dir = Path.Combine(
            artifactRoot,
            "job-" + job.JobId.ToString("D8"),
            "draft-" + SafeSegment(draftId));
        Directory.CreateDirectory(dir);

        var resumePath = Path.Combine(dir, SafeFileName(resume.FileName, "resume.pdf"));
        await File.WriteAllBytesAsync(resumePath, resume.Content, ct).ConfigureAwait(false);

        string? coverPath = null;
        if (cover is not null)
        {
            coverPath = Path.Combine(dir, SafeFileName(cover.FileName, "cover.pdf"));
            await File.WriteAllBytesAsync(coverPath, cover.Content, ct).ConfigureAwait(false);
        }

        var answersJson = AnswersJson(app);
        if (!string.IsNullOrWhiteSpace(answersJson))
            await File.WriteAllTextAsync(Path.Combine(dir, "answers.json"), answersJson, ct).ConfigureAwait(false);

        return new SavedArtifacts(resumePath, coverPath, answersJson);
    }

    private static string? AnswersJson(TailoredApplication app) =>
        app.Answers.Count == 0 ? null : JsonSerializer.Serialize(app.Answers);

    private static string SafeFileName(string fileName, string fallback)
    {
        var safe = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static string SafeSegment(string value)
    {
        var safe = string.Join("_", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? Guid.NewGuid().ToString("N") : safe;
    }

    private sealed record SavedArtifacts(string? ResumePath, string? CoverPath, string? AnswersJson);
}
