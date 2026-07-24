using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace SeekerSvc.Engine;

public static class ResumeTextExtractor
{
    public const long MaxResumeBytes = 20 * 1024 * 1024;
    public const int MaxExtractedCharacters = 150_000;

    public static async Task<string> ExtractAsync(string path, CancellationToken ct = default)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
            throw new FileNotFoundException("The resume file was not found.", path);
        if (file.Length == 0)
            throw new InvalidOperationException("The resume file is empty.");
        if (file.Length > MaxResumeBytes)
            throw new InvalidOperationException("The resume is larger than the 20 MB onboarding limit.");

        var extension = file.Extension.ToLowerInvariant();
        var rawText = extension switch
        {
            ".txt" or ".md" => await File.ReadAllTextAsync(path, ct).ConfigureAwait(false),
            ".docx" => ExtractDocx(path),
            ".pdf" => ExtractPdf(path),
            _ => throw new InvalidOperationException("Resume must be PDF, DOCX, TXT, or MD."),
        };

        var normalized = Normalize(rawText);
        if (normalized.Length < 40)
        {
            var hint = extension == ".pdf"
                ? " The PDF may contain scanned images instead of selectable text; try DOCX or TXT."
                : "";
            throw new InvalidOperationException("CareerSeeker could not extract enough resume text." + hint);
        }
        if (normalized.Length > MaxExtractedCharacters)
            throw new InvalidOperationException(
                $"The extracted resume text exceeds the {MaxExtractedCharacters:N0}-character onboarding limit.");
        return normalized;
    }

    public static string Normalize(string value)
    {
        var clean = new StringBuilder(value.Length);
        foreach (var ch in value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'))
        {
            if (ch == '\n' || ch == '\t' || !char.IsControl(ch))
                clean.Append(ch);
        }

        var lines = clean.ToString().Split('\n');
        var normalized = new StringBuilder(clean.Length);
        var blankLines = 0;
        foreach (var rawLine in lines)
        {
            var line = CollapseHorizontalWhitespace(rawLine).Trim();
            if (line.Length == 0)
            {
                blankLines++;
                if (blankLines <= 1 && normalized.Length > 0)
                    normalized.Append('\n');
                continue;
            }

            blankLines = 0;
            normalized.Append(line).Append('\n');
        }

        return normalized.ToString().Trim();
    }

    private static string ExtractPdf(string path)
    {
        using var document = PdfDocument.Open(path);
        var text = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            if (text.Length > 0) text.AppendLine();
            text.AppendLine(ContentOrderTextExtractor.GetText(page));
            if (text.Length > MaxExtractedCharacters)
                break;
        }
        return text.ToString();
    }

    private static string ExtractDocx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml")
                    ?? throw new InvalidOperationException("The DOCX file does not contain a Word document body.");
        if (entry.Length > 5 * 1024 * 1024)
            throw new InvalidOperationException("The DOCX document body exceeds the local extraction limit.");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = 5 * 1024 * 1024,
            XmlResolver = null,
        });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var text = new StringBuilder();

        foreach (var paragraph in document.Descendants(word + "p"))
        {
            foreach (var node in paragraph.Descendants())
            {
                if (node.Name == word + "t")
                    text.Append(node.Value);
                else if (node.Name == word + "tab")
                    text.Append('\t');
                else if (node.Name == word + "br" || node.Name == word + "cr")
                    text.AppendLine();
            }
            text.AppendLine();
        }

        return text.ToString();
    }

    private static string CollapseHorizontalWhitespace(string value)
    {
        var collapsed = new StringBuilder(value.Length);
        var inWhitespace = false;
        foreach (var ch in value)
        {
            if (ch is ' ' or '\t' or '\u00a0')
            {
                if (!inWhitespace) collapsed.Append(' ');
                inWhitespace = true;
                continue;
            }

            collapsed.Append(ch);
            inWhitespace = false;
        }
        return collapsed.ToString();
    }
}
