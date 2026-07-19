using System.Globalization;
using System.Text;
using SeekerSvc.Pipeline;

namespace SeekerSvc.Dispatcher;

/// <summary>
/// Deterministic ATS-clean PDF renderer for alpha drafts. It writes selectable, single-column text with
/// standard PDF fonts and no external runtime dependency. The renderer formats only Gate-cleared text;
/// it does not author new candidate claims.
/// </summary>
public sealed class AtsPdfDocumentRenderer : IDocumentRenderer
{
    private readonly AtsPdfRendererOptions _options;

    public AtsPdfDocumentRenderer(AtsPdfRendererOptions? options = null)
    {
        _options = options ?? new AtsPdfRendererOptions();
    }

    public Task<Attachment> RenderResumeAsync(PipelineJob job, TailoredApplication app, CancellationToken ct = default)
    {
        var title = $"{_options.CandidateName} - Resume";
        var fileName = SafeFileName($"{_options.CandidateName} - {job.Company} - Resume.pdf");
        var pdf = SimpleTextPdf.Write(title, app.ResumeText, _options);
        return Task.FromResult(new Attachment(fileName, "application/pdf", pdf));
    }

    public Task<Attachment?> RenderCoverAsync(PipelineJob job, TailoredApplication app, CancellationToken ct = default)
    {
        if (!_options.RenderCoverPdf) return Task.FromResult<Attachment?>(null);

        var title = $"{_options.CandidateName} - Cover Letter";
        var fileName = SafeFileName($"{_options.CandidateName} - {job.Company} - Cover Letter.pdf");
        var pdf = SimpleTextPdf.Write(title, app.CoverText, _options);
        return Task.FromResult<Attachment?>(new Attachment(fileName, "application/pdf", pdf));
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        return string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

public sealed record AtsPdfRendererOptions(
    string CandidateName = "Candidate",
    bool RenderCoverPdf = false,
    int FontSize = 11,
    int Margin = 54);

static class SimpleTextPdf
{
    private const int PageWidth = 612;
    private const int PageHeight = 792;
    private const int LineHeight = 14;

    public static byte[] Write(string title, string body, AtsPdfRendererOptions options)
    {
        var lines = BuildLines(title, body, options).ToArray();
        var linesPerPage = Math.Max(1, (PageHeight - options.Margin * 2) / LineHeight);
        var pages = lines.Chunk(linesPerPage).Select(chunk => chunk.ToArray()).ToArray();
        if (pages.Length == 0) pages = new[] { Array.Empty<string>() };

        var objects = new List<string>();
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");

        var firstPageObj = 3;
        var pageObjectNumbers = Enumerable.Range(0, pages.Length).Select(i => firstPageObj + i * 2).ToArray();
        objects.Add("<< /Type /Pages /Kids [" + string.Join(" ", pageObjectNumbers.Select(n => $"{n} 0 R")) +
                    $"] /Count {pages.Length} >>");

        for (var i = 0; i < pages.Length; i++)
        {
            var pageObj = firstPageObj + i * 2;
            var contentObj = pageObj + 1;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 {firstPageObj + pages.Length * 2} 0 R >> >> /Contents {contentObj} 0 R >>");
            objects.Add(ContentStream(pages[i], options));
        }

        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        return Assemble(objects);
    }

    private static IEnumerable<string> BuildLines(string title, string body, AtsPdfRendererOptions options)
    {
        foreach (var line in Wrap(Sanitize(title), options.FontSize, bold: true))
            yield return line;
        yield return "";

        foreach (var paragraph in NormalizeNewlines(body).Split('\n'))
        {
            var clean = Sanitize(paragraph).TrimEnd();
            if (clean.Length == 0)
            {
                yield return "";
                continue;
            }

            foreach (var line in Wrap(clean, options.FontSize, bold: false))
                yield return line;
        }
    }

    private static IEnumerable<string> Wrap(string text, int fontSize, bool bold)
    {
        var maxChars = Math.Max(45, (int)Math.Floor((PageWidth - 108) / (fontSize * 0.50)));
        if (bold) maxChars = Math.Max(35, maxChars - 10);

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            yield return "";
            yield break;
        }

        var line = new StringBuilder();
        foreach (var word in words)
        {
            if (word.Length > maxChars)
            {
                if (line.Length > 0)
                {
                    yield return line.ToString();
                    line.Clear();
                }

                for (var i = 0; i < word.Length; i += maxChars)
                    yield return word.Substring(i, Math.Min(maxChars, word.Length - i));
                continue;
            }

            if (line.Length > 0 && line.Length + 1 + word.Length > maxChars)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }

        if (line.Length > 0) yield return line.ToString();
    }

    private static string ContentStream(IReadOnlyList<string> lines, AtsPdfRendererOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BT");
        sb.Append(CultureInfo.InvariantCulture, $"/F1 {options.FontSize} Tf");
        sb.AppendLine();
        sb.Append(CultureInfo.InvariantCulture, $"{options.Margin} {PageHeight - options.Margin} Td");
        sb.AppendLine();
        sb.Append(CultureInfo.InvariantCulture, $"{LineHeight} TL");
        sb.AppendLine();
        foreach (var line in lines)
        {
            sb.Append('(').Append(EscapePdfString(line)).AppendLine(") Tj");
            sb.AppendLine("T*");
        }
        sb.AppendLine("ET");

        var bytes = Encoding.ASCII.GetBytes(sb.ToString());
        return $"<< /Length {bytes.Length} >>\nstream\n{sb}endstream";
    }

    private static byte[] Assemble(IReadOnlyList<string> objects)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, Encoding.ASCII, leaveOpen: true) { NewLine = "\n" };
        var offsets = new List<long> { 0 };

        writer.Write("%PDF-1.4\n");
        writer.Write("% CareerSeeker ATS PDF\n");
        writer.Flush();

        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            writer.Write(FormattableString.Invariant($"{i + 1} 0 obj\n"));
            writer.Write(objects[i]);
            writer.Write("\nendobj\n");
            writer.Flush();
        }

        var xref = ms.Position;
        writer.Write(FormattableString.Invariant($"xref\n0 {objects.Count + 1}\n"));
        writer.Write("0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            writer.Write(FormattableString.Invariant($"{offset:0000000000} 00000 n \n"));
        writer.Write("trailer\n");
        writer.Write(FormattableString.Invariant($"<< /Size {objects.Count + 1} /Root 1 0 R >>\n"));
        writer.Write("startxref\n");
        writer.Write(FormattableString.Invariant($"{xref}\n"));
        writer.Write("%%EOF\n");
        writer.Flush();

        return ms.ToArray();
    }

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch == '\t')
            {
                sb.Append(' ');
            }
            else if (ch >= 32 && ch <= 126)
            {
                sb.Append(ch);
            }
            else if (ch is '\n')
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('-');
            }
        }
        return sb.ToString();
    }

    private static string EscapePdfString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("(", "\\(", StringComparison.Ordinal)
             .Replace(")", "\\)", StringComparison.Ordinal);
}
