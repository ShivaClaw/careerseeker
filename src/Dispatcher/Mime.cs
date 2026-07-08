using System.Text;

namespace SeekerSvc.Dispatcher;

/// <summary>Web-safe base64 (RFC 4648 §5), padding stripped — the form Gmail's drafts.create wants for message.raw.</summary>
public static class Base64Url
{
    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        switch (t.Length % 4) { case 2: t += "=="; break; case 3: t += "="; break; }
        return Convert.FromBase64String(t);
    }
}

/// <summary>
/// Builds a complete RFC 5322 message (multipart/mixed: a text body plus file attachments) and returns
/// it base64url-encoded for Gmail. Pure string/byte work — no I/O — so the exact wire format is verified
/// offline by round-tripping. The Dispatcher passes the result straight to <see cref="IGmailDraftClient"/>.
/// </summary>
public static class MimeBuilder
{
    /// <summary>Assemble the raw RFC 5322 text (before base64url). Exposed for testing the wire format.</summary>
    public static string BuildMessage(
        string fromName, string fromEmail, string toEmail, string subject, string bodyText,
        IReadOnlyList<Attachment> attachments, DateTimeOffset? dateUtc = null)
    {
        var boundary = "cs_" + Guid.NewGuid().ToString("N");
        var sb = new StringBuilder();

        sb.Append("From: ").Append(FormatAddress(fromName, fromEmail)).Append("\r\n");
        sb.Append("To: ").Append(toEmail).Append("\r\n");
        sb.Append("Subject: ").Append(EncodeHeader(subject)).Append("\r\n");
        sb.Append("Date: ").Append((dateUtc ?? DateTimeOffset.UtcNow).ToString("r")).Append("\r\n");
        sb.Append("MIME-Version: 1.0\r\n");
        sb.Append("Content-Type: multipart/mixed; boundary=\"").Append(boundary).Append("\"\r\n");
        sb.Append("\r\n");

        // text/plain body, base64 so any UTF-8 content is transmitted intact
        sb.Append("--").Append(boundary).Append("\r\n");
        sb.Append("Content-Type: text/plain; charset=\"utf-8\"\r\n");
        sb.Append("Content-Transfer-Encoding: base64\r\n\r\n");
        sb.Append(Wrap(Convert.ToBase64String(Encoding.UTF8.GetBytes(bodyText)))).Append("\r\n");

        foreach (var a in attachments)
        {
            sb.Append("--").Append(boundary).Append("\r\n");
            sb.Append("Content-Type: ").Append(a.MimeType).Append("; name=\"").Append(a.FileName).Append("\"\r\n");
            sb.Append("Content-Transfer-Encoding: base64\r\n");
            sb.Append("Content-Disposition: attachment; filename=\"").Append(a.FileName).Append("\"\r\n\r\n");
            sb.Append(Wrap(Convert.ToBase64String(a.Content))).Append("\r\n");
        }

        sb.Append("--").Append(boundary).Append("--\r\n");
        return sb.ToString();
    }

    /// <summary>The base64url-encoded message, ready for message.raw.</summary>
    public static string BuildRaw(
        string fromName, string fromEmail, string toEmail, string subject, string bodyText,
        IReadOnlyList<Attachment> attachments, DateTimeOffset? dateUtc = null) =>
        Base64Url.Encode(Encoding.UTF8.GetBytes(
            BuildMessage(fromName, fromEmail, toEmail, subject, bodyText, attachments, dateUtc)));

    private static string FormatAddress(string name, string email) =>
        string.IsNullOrWhiteSpace(name) ? email : $"{EncodeHeader(name)} <{email}>";

    // RFC 2047 encoded-word for non-ASCII headers; plain ASCII passes through unchanged.
    private static string EncodeHeader(string s) =>
        s.All(c => c < 128) ? s : "=?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(s)) + "?=";

    // wrap base64 at 76 chars per RFC 2045
    private static string Wrap(string b64)
    {
        var sb = new StringBuilder(b64.Length + b64.Length / 76 * 2);
        for (var i = 0; i < b64.Length; i += 76)
            sb.Append(b64, i, Math.Min(76, b64.Length - i)).Append("\r\n");
        return sb.ToString().TrimEnd('\r', '\n');
    }
}
