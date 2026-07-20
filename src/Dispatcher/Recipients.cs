using System.Text.RegularExpressions;
using SeekerSvc.Pipeline;

namespace SeekerSvc.Dispatcher;

/// <summary>
/// Pulls the application email out of an email-channel posting ("send your resume to jobs@acme.com").
/// Deterministic: address regex, ranked by proximity to apply keywords, with no-reply addresses
/// ignored. Returns null when nothing credible is found — the packager then refuses to build a blank
/// email draft and downgrades to a manual-finish package instead.
/// </summary>
public static class RecipientExtractor
{
    private static readonly Regex Email = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

    private static readonly string[] Keywords =
        { "send", "email", "e-mail", "resume", "cv", "apply", "application", "submit", "contact", "to" };

    public static string? Extract(string? postingText)
    {
        if (string.IsNullOrWhiteSpace(postingText)) return null;

        string? best = null;
        var bestScore = int.MinValue;
        foreach (Match m in Email.Matches(postingText))
        {
            var addr = m.Value;
            var score = 0;

            if (IsNoReplyMailbox(addr)) continue;

            // proximity: any apply keyword within ~40 chars before the address
            var windowStart = Math.Max(0, m.Index - 40);
            var window = postingText.Substring(windowStart, m.Index - windowStart).ToLowerInvariant();
            if (Keywords.Any(k => window.Contains(k))) score += 10;

            // prefer role mailboxes
            var low = addr.ToLowerInvariant();
            if (low.StartsWith("jobs@") || low.StartsWith("careers@") || low.StartsWith("recruiting@") ||
                low.StartsWith("hr@") || low.StartsWith("hiring@")) score += 5;

            if (score > bestScore) { bestScore = score; best = addr; }
        }
        return best;
    }

    private static bool IsNoReplyMailbox(string addr)
    {
        var low = addr.ToLowerInvariant();
        return low.Contains("noreply")
            || low.Contains("no-reply")
            || low.Contains("donotreply")
            || low.Contains("do-not-reply");
    }
}

/// <summary>
/// Classifies how an application is delivered from its apply URL / posting (spec §6 channel strategy).
/// Big-three ATS hosts are auto-fillable later (L2); LinkedIn/Indeed are read-only boards we never
/// auto-submit to; a named email is the Gmail-native path. Used as a fallback when the Store has not
/// already classified the posting.
/// </summary>
public static class ChannelDetector
{
    private static readonly string[] AtsHosts =
        { "job-boards.greenhouse.io", "boards.greenhouse.io", "greenhouse.io", "lever.co", "jobs.lever.co", "ashbyhq.com", "workable.com" };

    private static readonly string[] ReadOnlyBoards =
        { "linkedin.com", "indeed.com" };

    public static DispatchChannel Detect(string? applyUrl, string? applicationEmail)
    {
        if (!string.IsNullOrWhiteSpace(applicationEmail)) return DispatchChannel.Email;

        var url = (applyUrl ?? "").ToLowerInvariant();
        if (url.StartsWith("mailto:")) return DispatchChannel.Email;
        if (ReadOnlyBoards.Any(url.Contains)) return DispatchChannel.ManualFinish;
        if (AtsHosts.Any(url.Contains)) return DispatchChannel.AtsForm;
        if (!string.IsNullOrWhiteSpace(applyUrl)) return DispatchChannel.AtsForm; // generic career-page form
        return DispatchChannel.ManualFinish;
    }

    /// <summary>Pull the address out of a mailto: apply URL, if that is how the posting expressed it.</summary>
    public static string? MailtoAddress(string? applyUrl)
    {
        if (string.IsNullOrWhiteSpace(applyUrl)) return null;
        if (!applyUrl.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return null;
        var addr = applyUrl.Substring("mailto:".Length);
        var q = addr.IndexOf('?');
        var path = q >= 0 ? addr[..q] : addr;
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(path).Trim();
        }
        catch (UriFormatException)
        {
            return null;
        }

        if (decoded.Contains('\r') || decoded.Contains('\n')) return null;
        var recipient = RecipientExtractor.Extract(decoded);
        return string.Equals(decoded, recipient, StringComparison.OrdinalIgnoreCase)
            ? recipient
            : null;
    }

    /// <summary>
    /// Resolve an email recipient for selected-job drafting without letting incidental posting contact
    /// addresses override a concrete HTTP apply URL.
    /// </summary>
    public static string? ResolveApplicationEmail(string? applyUrl, string? postingText)
    {
        var mailto = MailtoAddress(applyUrl);
        if (!string.IsNullOrWhiteSpace(mailto)) return mailto;
        return string.IsNullOrWhiteSpace(applyUrl)
            ? RecipientExtractor.Extract(postingText)
            : null;
    }
}
