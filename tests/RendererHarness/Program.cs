using System.Text;
using SeekerSvc.Dispatcher;
using SeekerSvc.Pipeline;
using SeekerSvc.Verifier;

int passed = 0, failed = 0;
void Check(string name, bool condition, string? detail = null)
{
    if (condition)
    {
        passed++;
        Console.WriteLine($"  PASS  {name}");
    }
    else
    {
        failed++;
        Console.WriteLine($"  FAIL  {name}{(detail is null ? "" : $"  -- {detail}")}");
    }
}

Console.WriteLine("=== CareerSeeker ATS PDF renderer harness ===\n");

var outputDir = Path.Combine("output", "pdf");
Directory.CreateDirectory(outputDir);

var renderer = new AtsPdfDocumentRenderer(new AtsPdfRendererOptions("Jordan Lee", RenderCoverPdf: true));
var job = new PipelineJob(77, "Senior Software Engineer", "Acme");
var app = new TailoredApplication(
    new[]
    {
        new TailoredClaim(ClaimKind.Title, "Senior Software Engineer"),
        new TailoredClaim(ClaimKind.Skill, "distributed systems"),
    },
    """
    Jordan Lee
    Senior Software Engineer

    Experience
    Built reliable distributed systems in Go.
    Reduced p99 latency 30% through observability and service tuning.

    Skills
    Go, distributed systems, reliability, mentoring
    """,
    "I am excited to apply. I have built reliable distributed systems in Go and would bring that experience to your team.",
    new Dictionary<string, string>());

var resume = await renderer.RenderResumeAsync(job, app);
var cover = await renderer.RenderCoverAsync(job, app);

var resumePath = Path.Combine(outputDir, "renderer-harness-resume.pdf");
var coverPath = Path.Combine(outputDir, "renderer-harness-cover.pdf");
await File.WriteAllBytesAsync(resumePath, resume.Content);
if (cover is not null) await File.WriteAllBytesAsync(coverPath, cover.Content);

var resumeAscii = Encoding.ASCII.GetString(resume.Content);
Check("resume PDF starts with a PDF header", resumeAscii.StartsWith("%PDF-1.4", StringComparison.Ordinal));
Check("resume PDF has xref and EOF markers",
    resumeAscii.Contains("xref", StringComparison.Ordinal) && resumeAscii.Contains("%%EOF", StringComparison.Ordinal));
Check("resume PDF preserves selectable resume text",
    resumeAscii.Contains("Built reliable distributed systems in Go.", StringComparison.Ordinal)
    && resumeAscii.Contains("Reduced p99 latency 30%", StringComparison.Ordinal));
Check("resume attachment uses PDF metadata",
    resume.MimeType == "application/pdf" && resume.FileName == "Jordan Lee - Acme - Resume.pdf");
Check("cover PDF is optional but renderable",
    cover is not null
    && cover.MimeType == "application/pdf"
    && Encoding.ASCII.GetString(cover.Content).Contains("I am excited to apply.", StringComparison.Ordinal));
Check("sample PDFs written for visual inspection",
    File.Exists(resumePath) && File.Exists(coverPath),
    $"{resumePath}; {coverPath}");

Console.WriteLine($"\nArtifacts:");
Console.WriteLine("  " + resumePath);
Console.WriteLine("  " + coverPath);
Console.WriteLine($"\n=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;
