using SeekerSvc.Pipeline;

namespace SeekerSvc.Dispatcher;

/// <summary>A file attached to a draft (e.g. the rendered resume PDF).</summary>
public sealed record Attachment(string FileName, string MimeType, byte[] Content);

/// <summary>
/// The channel-agnostic, reviewable assembly of one application before it becomes a Gmail draft. Built
/// purely from the Gate-cleared <see cref="TailoredApplication"/> plus posting metadata — the Dispatcher
/// authors no claim-bearing prose of its own (only the templated subject and, for manual-finish drafts,
/// instructions addressed to the user). That faithful-packaging property is the Dispatcher's safety
/// contribution: it cannot introduce a claim the Fabrication Gate never saw.
/// </summary>
public sealed record DraftPackage(
    DispatchChannel Channel,
    string? Recipient,                       // email-channel To:; for manual-finish this is the user's own address
    string Subject,
    string BodyText,                         // email-channel: the cover letter verbatim; manual-finish: user instructions
    IReadOnlyList<Attachment> Attachments,
    string? ApplyUrl = null,                 // manual-finish: where the user completes the application
    IReadOnlyList<string>? ManualSteps = null);

/// <summary>Posting facts the Dispatcher needs that the lean <see cref="PipelineJob"/> does not carry.</summary>
public sealed record PostingDispatchInfo(
    DispatchChannel Channel,
    string? ApplicationEmail = null,         // email-channel recipient, if the posting named one
    string? ApplyUrl = null,                 // ATS / career-page / read-only-board URL
    string? PostingText = null);             // raw posting body, for recipient extraction fallback

/// <summary>Sender identity and packaging options. No claim content — identity + formatting only.</summary>
public sealed record DispatcherConfig(
    string CandidateName,
    string CandidateEmail,                   // the user's own Gmail address (From, and self-draft To)
    string SubjectTemplate = "Application for {title}",
    string OutboxLabel = "CareerSeeker/Outbox",
    string ActionNeededLabel = "CareerSeeker/Action-Needed",
    bool AttachCoverPdf = false);

/// <summary>
/// Source of posting dispatch facts (channel, recipient, apply URL). Backed by the Store in production;
/// faked in tests. Keeping this a port leaves the Pipeline's <see cref="IDispatcher"/> signature intact.
/// </summary>
public interface IPostingSource
{
    Task<PostingDispatchInfo> GetDispatchInfoAsync(long jobId, CancellationToken ct = default);
}

/// <summary>
/// Renders the application's documents into attachments (resume HTML → PDF, per spec §5.5). The real
/// implementation drives headless Chromium and is wired at integration; tests use a fake that returns
/// bytes. The Dispatcher only attaches what this returns — it does not generate document content.
/// </summary>
public interface IDocumentRenderer
{
    Task<Attachment> RenderResumeAsync(PipelineJob job, TailoredApplication app, CancellationToken ct = default);
    Task<Attachment?> RenderCoverAsync(PipelineJob job, TailoredApplication app, CancellationToken ct = default);
}

/// <summary>
/// The narrow Gmail surface L1 needs: create a draft and ensure a label exists. Scope is
/// <c>gmail.compose</c> — there is deliberately <b>no Send method on this interface</b>, so an L1 build
/// is structurally incapable of sending mail. Sending belongs to L2/L3 behind a separate, gated port.
/// The real client calls users.drafts.create; tests use a fake.
/// </summary>
public interface IGmailDraftClient
{
    /// <summary>Create a draft from a base64url-encoded RFC 5322 message. Returns the draft id.</summary>
    Task<string> CreateDraftAsync(string rawRfc822Base64Url, IReadOnlyList<string> labelIds, CancellationToken ct = default);

    /// <summary>Resolve a label path to an id, creating the CareerSeeker/* tree if needed. Returns the label id.</summary>
    Task<string> EnsureLabelAsync(string labelPath, CancellationToken ct = default);
}
