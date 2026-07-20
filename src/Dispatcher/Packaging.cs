using System.Text;
using SeekerSvc.Pipeline;

namespace SeekerSvc.Dispatcher;

/// <summary>
/// Assembles a <see cref="DraftPackage"/> from a Gate-cleared application and posting metadata. Pure: no
/// I/O, no model, no clock — so packaging is fully checkable offline. Two rules give the Dispatcher its
/// safety character:
///   • <b>Faithful packaging.</b> For an email-channel application the body is the cover letter verbatim;
///     the only Dispatcher-authored text sent to an employer is the templated subject (job title/company,
///     no candidate claims). The packager cannot smuggle in a claim the Gate never verified.
///   • <b>No blank emails.</b> If the channel is Email but no credible recipient can be resolved, the
///     package downgrades to a manual-finish self-draft rather than addressing mail to nobody.
/// </summary>
public static class PackageBuilder
{
    public static DraftPackage Build(
        PipelineJob job,
        TailoredApplication app,
        PostingDispatchInfo info,
        DispatcherConfig config,
        Attachment resume,
        Attachment? coverPdf = null)
    {
        var attachments = new List<Attachment> { resume };
        if (config.AttachCoverPdf && coverPdf is not null) attachments.Add(coverPdf);

        var recipient = info.ApplicationEmail
            ?? ChannelDetector.MailtoAddress(info.ApplyUrl)
            ?? RecipientExtractor.Extract(info.PostingText);

        var channel = info.Channel;
        if (channel == DispatchChannel.Email && string.IsNullOrWhiteSpace(recipient))
            channel = DispatchChannel.ManualFinish; // refuse to draft an email with no recipient

        if (channel == DispatchChannel.Email)
        {
            return new DraftPackage(
                DispatchChannel.Email,
                recipient,
                Subject(config.SubjectTemplate, job),
                app.CoverText,                 // verbatim — the verified cover letter is the email body
                attachments);
        }

        // ATS form / read-only board: everything-but-submit. A self-addressed draft the user opens on any
        // device — download the resume, click the link, paste the answers, submit. Addressed to the user,
        // so the instructional body carries no employer-facing claims.
        return new DraftPackage(
            channel,
            config.CandidateEmail,
            $"Finish: {job.Title} at {job.Company} (2 min)",
            ManualBody(job, app, info),
            attachments,
            ApplyUrl: info.ApplyUrl,
            ManualSteps: ManualSteps(channel, info.ApplyUrl));
    }

    private static string Subject(string template, PipelineJob job) =>
        template.Replace("{title}", job.Title, StringComparison.OrdinalIgnoreCase)
                .Replace("{company}", job.Company, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ManualSteps(DispatchChannel channel, string? applyUrl)
    {
        var steps = new List<string> { "Open the apply link below." };
        steps.Add("Download and upload the attached resume PDF.");
        steps.Add("Paste the prepared answers below into the matching fields.");
        steps.Add(channel == DispatchChannel.ManualFinish
            ? "Complete any remaining fields and submit (this board can't be automated safely)."
            : "Review the form, complete any remaining fields, and submit when ready.");
        return steps;
    }

    private static string ManualBody(PipelineJob job, TailoredApplication app, PostingDispatchInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Your tailored application for {job.Title} at {job.Company} is ready to finish.");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(info.ApplyUrl))
        {
            sb.AppendLine("Apply here:");
            sb.AppendLine(info.ApplyUrl);
            sb.AppendLine();
        }
        foreach (var step in ManualSteps(info.Channel, info.ApplyUrl))
            sb.AppendLine("- " + step);

        if (app.Answers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Prepared answers:");
            foreach (var kv in app.Answers)
            {
                sb.AppendLine($"Q: {kv.Key}");
                sb.AppendLine($"A: {kv.Value}");
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }
}
