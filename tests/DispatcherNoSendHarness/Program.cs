using System.Reflection;
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

Console.WriteLine("=== CareerSeeker Dispatcher no-send harness ===\n");

static bool IsSendVerb(string name) =>
    name.Contains("send", StringComparison.OrdinalIgnoreCase);

var asm = typeof(Dispatcher).Assembly;
var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

var offenders = asm.GetTypes()
    .Where(t => t.IsPublic || t.IsNestedPublic)
    .SelectMany(t => t.GetMethods(flags).Select(m => $"{t.FullName}.{m.Name}"))
    .Where(sig => IsSendVerb(sig[(sig.LastIndexOf('.') + 1)..]))
    .Distinct()
    .OrderBy(s => s)
    .ToList();

Check("No public send-capable method anywhere in Dispatcher assembly",
    offenders.Count == 0,
    offenders.Count == 0 ? null : "found: " + string.Join(", ", offenders));

var gmailMethods = typeof(IGmailDraftClient).GetMethods();
var gmailSends = gmailMethods.Where(m => IsSendVerb(m.Name)).Select(m => m.Name).ToList();
Check("Gmail draft port exposes no send method",
    gmailSends.Count == 0,
    gmailSends.Count == 0 ? null : "offending: " + string.Join(", ", gmailSends));
Check("Gmail draft port exposes create-draft positive control",
    gmailMethods.Any(m => m.Name.Contains("Draft", StringComparison.OrdinalIgnoreCase)));

var dispatcherSends = typeof(Dispatcher).GetMethods(flags).Where(m => IsSendVerb(m.Name)).Select(m => m.Name).ToList();
Check("L1 Dispatcher exposes no send method",
    dispatcherSends.Count == 0,
    dispatcherSends.Count == 0 ? null : "offending: " + string.Join(", ", dispatcherSends));

var dispatcher = new Dispatcher(
    new FakePostings(new PostingDispatchInfo(DispatchChannel.Email, "jobs@example.com")),
    new FakeRenderer(),
    new FakeGmail(),
    new DispatcherConfig("Jordan Lee", "jordan@example.com"));

try
{
    await dispatcher.SubmitAsync(
        new PipelineJob(1, "Software Engineer", "Acme"),
        new TailoredApplication(Array.Empty<TailoredClaim>(), "resume", "cover", new Dictionary<string, string>()));
    Check("SubmitAsync throws NotSupportedException", false, "completed without throwing");
}
catch (NotSupportedException ex)
{
    Check("SubmitAsync throws NotSupportedException", ex.Message.Contains("gmail.compose", StringComparison.OrdinalIgnoreCase));
}

Console.WriteLine($"\n=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;

sealed class FakePostings : IPostingSource
{
    private readonly PostingDispatchInfo _info;
    public FakePostings(PostingDispatchInfo info) => _info = info;
    public Task<PostingDispatchInfo> GetDispatchInfoAsync(long jobId, CancellationToken ct = default) => Task.FromResult(_info);
}

sealed class FakeRenderer : IDocumentRenderer
{
    public Task<Attachment> RenderResumeAsync(PipelineJob job, TailoredApplication app, CancellationToken ct = default)
        => Task.FromResult(new Attachment("resume.pdf", "application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }));

    public Task<Attachment?> RenderCoverAsync(PipelineJob job, TailoredApplication app, CancellationToken ct = default)
        => Task.FromResult<Attachment?>(null);
}

sealed class FakeGmail : IGmailDraftClient
{
    public Task<string> CreateDraftAsync(string rawRfc822Base64Url, IReadOnlyList<string> labelIds, CancellationToken ct = default)
        => Task.FromResult("draft_1");

    public Task<string> EnsureLabelAsync(string labelPath, CancellationToken ct = default)
        => Task.FromResult("Label_" + labelPath);
}
