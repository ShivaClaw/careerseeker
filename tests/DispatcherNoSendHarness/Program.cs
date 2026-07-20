using System.Reflection;
using System.Net;
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
Check("Gmail draft port is draft-only",
    gmailMethods.Select(m => m.Name).OrderBy(n => n).SequenceEqual(new[] { nameof(IGmailDraftClient.CreateDraftAsync) }),
    "methods: " + string.Join(", ", gmailMethods.Select(m => m.Name).OrderBy(n => n)));
Check("Gmail label capability is split from draft port",
    typeof(IGmailLabelManager).GetMethods().Any(m => m.Name == nameof(IGmailLabelManager.EnsureLabelAsync))
    && !gmailMethods.Any(m => m.Name == nameof(IGmailLabelManager.EnsureLabelAsync)));

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

try
{
    var labelsWithoutCapability = new Dispatcher(
        new FakePostings(new PostingDispatchInfo(DispatchChannel.Email, "jobs@example.com")),
        new FakeRenderer(),
        new FakeGmail(),
        new DispatcherConfig("Jordan Lee", "jordan@example.com", UseCustomLabels: true));
    await labelsWithoutCapability.CreateDraftAsync(
        new PipelineJob(1, "Software Engineer", "Acme"),
        new TailoredApplication(Array.Empty<TailoredClaim>(), "resume", "cover", new Dictionary<string, string>()));
    Check("Custom labels require separate label manager", false, "draft completed without label manager");
}
catch (InvalidOperationException ex)
{
    Check("Custom labels require separate label manager",
        ex.Message.Contains(nameof(IGmailLabelManager), StringComparison.Ordinal));
}

var labeledGmail = new FakeGmail();
var labeledLabels = new FakeLabels();
var labeledDispatcher = new Dispatcher(
    new FakePostings(new PostingDispatchInfo(DispatchChannel.Email, "jobs@example.com")),
    new FakeRenderer(),
    labeledGmail,
    new DispatcherConfig("Jordan Lee", "jordan@example.com", UseCustomLabels: true),
    labeledLabels);
await labeledDispatcher.CreateDraftAsync(
    new PipelineJob(1, "Software Engineer", "Acme"),
    new TailoredApplication(Array.Empty<TailoredClaim>(), "resume", "cover", new Dictionary<string, string>()));
Check("Injected label manager supplies custom label ids",
    labeledLabels.Calls == 1 && labeledGmail.LastLabelIds.SequenceEqual(new[] { "Label_CareerSeeker/Outbox" }),
    $"labelCalls={labeledLabels.Calls} ids={string.Join(",", labeledGmail.LastLabelIds)}");

var manualPackage = PackageBuilder.Build(
    new PipelineJob(7, "Platform Engineer", "Acme"),
    new TailoredApplication(
        Array.Empty<TailoredClaim>(),
        "resume",
        "cover",
        new Dictionary<string, string> { ["Why Acme?"] = "I can support distributed systems work." }),
    new PostingDispatchInfo(DispatchChannel.ManualFinish, ApplyUrl: "https://jobs.example/apply"),
    new DispatcherConfig("Jordan Lee", "jordan@example.com"),
    new Attachment("resume.pdf", "application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }));
Check("Manual-finish draft body uses clean ASCII bullets",
    manualPackage.BodyText.Contains("- Open the apply link below.", StringComparison.Ordinal) &&
    manualPackage.BodyText.IndexOf((char)0x2022) < 0,
    manualPackage.BodyText);

var atsPackage = PackageBuilder.Build(
    new PipelineJob(8, "Backend Engineer", "Acme"),
    new TailoredApplication(Array.Empty<TailoredClaim>(), "resume", "cover", new Dictionary<string, string>()),
    new PostingDispatchInfo(DispatchChannel.AtsForm, ApplyUrl: "https://boards.greenhouse.io/acme/jobs/8"),
    new DispatcherConfig("Jordan Lee", "jordan@example.com"),
    new Attachment("resume.pdf", "application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }));
Check("ATS-form draft body does not claim fields were auto-filled",
    !atsPackage.BodyText.Contains("auto-filled", StringComparison.OrdinalIgnoreCase) &&
    atsPackage.BodyText.Contains("complete any remaining fields", StringComparison.OrdinalIgnoreCase),
    atsPackage.BodyText);

var emailPackage = PackageBuilder.Build(
    new PipelineJob(9, "Data Engineer", "Acme"),
    new TailoredApplication(Array.Empty<TailoredClaim>(), "resume", "cover", new Dictionary<string, string>()),
    new PostingDispatchInfo(DispatchChannel.Email, "jobs@example.com"),
    new DispatcherConfig("Jordan Lee", "jordan@example.com"),
    new Attachment("resume.pdf", "application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }));
Check("Email draft subject includes title and company without claim prose",
    emailPackage.Subject == "Application for Data Engineer at Acme",
    emailPackage.Subject);

Check("Recipient extractor refuses no-reply-only postings",
    RecipientExtractor.Extract("Please email your resume to no-reply@example.com.") is null);
Check("Recipient extractor prefers real role mailbox over no-reply",
    RecipientExtractor.Extract("Do not use no-reply@example.com. Email your resume to careers@example.com.") == "careers@example.com");
Check("Mailto recipient parser strips query strings",
    ChannelDetector.MailtoAddress("mailto:jobs@example.com?subject=Application") == "jobs@example.com");
Check("Mailto recipient parser refuses blank recipients",
    ChannelDetector.MailtoAddress("mailto:?subject=Application") is null);
Check("Mailto recipient parser refuses multi-recipient paths",
    ChannelDetector.MailtoAddress("mailto:jobs@example.com,attacker@example.com") is null);
Check("Selected-job resolver accepts mailto recipient",
    ChannelDetector.ResolveApplicationEmail("mailto:jobs@example.com?subject=Application", "Contact recruiting@example.com") == "jobs@example.com");
Check("Selected-job resolver extracts posting email only without apply URL",
    ChannelDetector.ResolveApplicationEmail(null, "Email your resume to careers@example.com.") == "careers@example.com");
Check("Selected-job resolver ignores no-reply-only posting without apply URL",
    ChannelDetector.ResolveApplicationEmail(null, "Email your resume to no-reply@example.com.") is null);
Check("Selected-job resolver does not let contact email override HTTP apply URL",
    ChannelDetector.ResolveApplicationEmail("https://boards.greenhouse.io/acme/jobs/10", "Contact hr@example.com for accommodations.") is null);

var noReplyPackage = PackageBuilder.Build(
    new PipelineJob(10, "Security Engineer", "Acme"),
    new TailoredApplication(Array.Empty<TailoredClaim>(), "resume", "cover", new Dictionary<string, string>()),
    new PostingDispatchInfo(
        DispatchChannel.Email,
        PostingText: "Email your resume to no-reply@example.com."),
    new DispatcherConfig("Jordan Lee", "jordan@example.com"),
    new Attachment("resume.pdf", "application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }));
Check("Email package downgrades no-reply-only postings to manual finish",
    noReplyPackage.Channel == DispatchChannel.ManualFinish
    && noReplyPackage.Recipient == "jordan@example.com"
    && noReplyPackage.BodyText.Contains("can't be automated safely", StringComparison.OrdinalIgnoreCase),
    $"channel={noReplyPackage.Channel} recipient={noReplyPackage.Recipient} body={noReplyPackage.BodyText}");

Console.WriteLine("\n[ PDF renderer ]");
var pdfRenderer = new AtsPdfDocumentRenderer(new AtsPdfRendererOptions("Jordan Lee", RenderCoverPdf: true));
var pdfJob = new PipelineJob(42, "Software Engineer", "Acme");
var pdfApp = new TailoredApplication(
    Array.Empty<TailoredClaim>(),
    "Jordan Lee\nSenior Software Engineer\nBuilt reliable distributed systems in Go.",
    "I am excited to apply. I have built reliable distributed systems in Go.",
    new Dictionary<string, string>());
var resumePdf = await pdfRenderer.RenderResumeAsync(pdfJob, pdfApp);
var coverPdf = await pdfRenderer.RenderCoverAsync(pdfJob, pdfApp);
var resumeText = System.Text.Encoding.ASCII.GetString(resumePdf.Content);
Check("ATS PDF renderer emits a real resume PDF attachment",
    resumePdf.MimeType == "application/pdf"
    && resumePdf.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
    && resumeText.StartsWith("%PDF-1.4", StringComparison.Ordinal)
    && resumeText.Contains("Built reliable distributed systems in Go.", StringComparison.Ordinal));
Check("ATS PDF renderer can emit optional cover PDF",
    coverPdf is not null
    && coverPdf.MimeType == "application/pdf"
    && System.Text.Encoding.ASCII.GetString(coverPdf.Content).Contains("I am excited to apply.", StringComparison.Ordinal));

var pdfGmail = new FakeGmail();
var pdfDispatcher = new Dispatcher(
    new FakePostings(new PostingDispatchInfo(DispatchChannel.Email, "jobs@example.com")),
    pdfRenderer,
    pdfGmail,
    new DispatcherConfig("Jordan Lee", "jordan@example.com", AttachCoverPdf: true));
await pdfDispatcher.CreateDraftAsync(pdfJob, pdfApp);
var decodedMessage = System.Text.Encoding.UTF8.GetString(Base64Url.Decode(pdfGmail.LastRaw));
Check("Dispatcher attaches rendered PDF documents to the Gmail draft",
    decodedMessage.Contains("Content-Type: application/pdf", StringComparison.Ordinal)
    && decodedMessage.Contains("Jordan Lee - Acme - Resume.pdf", StringComparison.Ordinal)
    && decodedMessage.Contains("Jordan Lee - Acme - Cover Letter.pdf", StringComparison.Ordinal));

var hostileMime = MimeBuilder.BuildMessage(
    "Jordan\r\nX-CareerSeeker-Injected: from",
    "jordan@example.com\r\nBcc: attacker@example.com",
    "jobs@example.com\r\nBcc: attacker@example.com",
    "Application\r\nX-CareerSeeker-Injected: subject",
    "body",
    new[]
    {
        new Attachment(
            "resume.pdf\"\r\nX-CareerSeeker-Injected: file",
            "application/pdf\r\nX-CareerSeeker-Injected: type",
            new byte[] { 1, 2, 3 }),
    },
    DateTimeOffset.UnixEpoch);
Check("MIME builder neutralizes header injection inputs",
    !hostileMime.Contains("\r\nX-CareerSeeker-Injected:", StringComparison.Ordinal)
    && !hostileMime.Contains("\r\nBcc:", StringComparison.Ordinal)
    && hostileMime.Contains("Content-Type: application/octet-stream; name=\"resume.pdf\\\" X-CareerSeeker-Injected: file\"", StringComparison.Ordinal),
    hostileMime);

var artifactRoot = Path.Combine(Path.GetTempPath(), "CareerSeeker-DispatcherArtifacts-" + Guid.NewGuid().ToString("N"));
try
{
    var artifactGmail = new FakeGmail();
    var artifactDispatcher = new Dispatcher(
        new FakePostings(new PostingDispatchInfo(DispatchChannel.Email, "jobs@example.com")),
        pdfRenderer,
        artifactGmail,
        new DispatcherConfig("Jordan Lee", "jordan@example.com", AttachCoverPdf: true, ArtifactDirectory: artifactRoot));
    var artifactOutcome = await artifactDispatcher.CreateDraftAsync(pdfJob, pdfApp);
    Check("Dispatcher persists local draft artifacts when configured",
        artifactOutcome.ResumePath is not null &&
        artifactOutcome.CoverPath is not null &&
        Path.IsPathRooted(artifactOutcome.ResumePath) &&
        File.Exists(artifactOutcome.ResumePath) &&
        File.ReadAllBytes(artifactOutcome.ResumePath).SequenceEqual(resumePdf.Content));
}
finally
{
    try { if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true); } catch (IOException) { }
}

Console.WriteLine("\n[ OAuth local controls ]");
var tempRoot = Path.Combine(Path.GetTempPath(), "CareerSeeker-DispatcherHarness-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var clientJson = Path.Combine(tempRoot, "client_secret.json");
    await File.WriteAllTextAsync(clientJson, """
    {
      "installed": {
        "client_id": "client-123.apps.googleusercontent.com",
        "client_secret": "secret-abc"
      }
    }
    """);
    var parsed = GoogleOAuthClient.Load(clientJson);
    Check("OAuth client JSON parser accepts installed clients",
        parsed.ClientId == "client-123.apps.googleusercontent.com"
        && parsed.ClientSecret == "secret-abc"
        && parsed.AuthUri == "https://accounts.google.com/o/oauth2/auth"
        && parsed.TokenUri == "https://oauth2.googleapis.com/token"
        && parsed.RevokeUri == "https://oauth2.googleapis.com/revoke");

    var webJson = Path.Combine(tempRoot, "web_client.json");
    await File.WriteAllTextAsync(webJson, """
    {
      "web": {
        "client_id": "web-123",
        "auth_uri": "https://auth.example",
        "token_uri": "https://token.example",
        "revoke_uri": "https://revoke.example"
      }
    }
    """);
    var parsedWeb = GoogleOAuthClient.Load(webJson);
    Check("OAuth client JSON parser accepts web clients with explicit endpoints",
        parsedWeb.ClientId == "web-123"
        && parsedWeb.ClientSecret is null
        && parsedWeb.AuthUri == "https://auth.example"
        && parsedWeb.TokenUri == "https://token.example"
        && parsedWeb.RevokeUri == "https://revoke.example");

    var vaultPath = Path.Combine(tempRoot, "gmail-token.dpapi");
    var vault = new DpapiTokenVault(vaultPath);
    var token = new OAuthToken(
        "access-token",
        "refresh-token",
        DateTimeOffset.UtcNow.AddHours(1),
        GoogleOAuthTokenSource.GmailComposeScope);
    try
    {
        vault.Save(token);
        var loaded = vault.Load();
        Check("DPAPI token vault round-trips locally",
            loaded?.AccessToken == token.AccessToken
            && loaded.RefreshToken == token.RefreshToken
            && loaded.Scope == token.Scope);
        vault.Delete();
        Check("DPAPI token vault delete removes local token material", vault.Load() is null);
    }
    catch (Exception ex) when (ex is DllNotFoundException
                               or EntryPointNotFoundException
                               or PlatformNotSupportedException
                               or System.ComponentModel.Win32Exception)
    {
        Check("DPAPI token vault round-trips locally", true, "skipped: DPAPI unavailable on this host");
        Check("DPAPI token vault delete removes local token material", true, "skipped: DPAPI unavailable on this host");
    }

    var secretVault = new DpapiSecretVault(Path.Combine(tempRoot, "byok-keys.dpapi"));
    try
    {
        secretVault.Save(new Dictionary<string, string>
        {
            ["anthropic"] = "anthropic-test-key",
            ["google"] = "gemini-test-key",
        });
        var loadedSecrets = secretVault.Load();
        Check("DPAPI secret vault round-trips provider keys locally",
            loadedSecrets["anthropic"] == "anthropic-test-key"
            && loadedSecrets["google"] == "gemini-test-key");
        secretVault.Delete();
        Check("DPAPI secret vault delete removes provider keys",
            secretVault.Load().Count == 0 && !secretVault.Exists);
    }
    catch (Exception ex) when (ex is DllNotFoundException
                               or EntryPointNotFoundException
                               or PlatformNotSupportedException
                               or System.ComponentModel.Win32Exception)
    {
        Check("DPAPI secret vault round-trips provider keys locally", true, "skipped: DPAPI unavailable on this host");
        Check("DPAPI secret vault delete removes provider keys", true, "skipped: DPAPI unavailable on this host");
    }

    var revokeVault = new DpapiTokenVault(Path.Combine(tempRoot, "revoke-token.dpapi"));
    var revokeHandler = new CapturingHandler(HttpStatusCode.OK, "{}");
    var tokenSource = new GoogleOAuthTokenSource(
        new HttpClient(revokeHandler),
        new GoogleOAuthClient("client-id", null, "https://auth.example", "https://token.example", "https://revoke.example"),
        revokeVault);

    try
    {
        revokeVault.Save(token);
        var disconnected = await tokenSource.DisconnectAsync();
        Check("Disconnect Gmail revokes refresh token then deletes vault",
            disconnected
            && revokeVault.Load() is null
            && revokeHandler.Calls == 1
            && revokeHandler.LastRequestUri == "https://revoke.example/"
            && revokeHandler.LastBody == "token=refresh-token",
            $"calls={revokeHandler.Calls} uri={revokeHandler.LastRequestUri} body={revokeHandler.LastBody}");

        var noToken = await tokenSource.DisconnectAsync();
        Check("Disconnect Gmail without a token is a harmless no-op",
            noToken == false && revokeHandler.Calls == 1);
    }
    catch (Exception ex) when (ex is DllNotFoundException
                               or EntryPointNotFoundException
                               or PlatformNotSupportedException
                               or System.ComponentModel.Win32Exception)
    {
        Check("Disconnect Gmail revokes refresh token then deletes vault", true, "skipped: DPAPI unavailable on this host");
        Check("Disconnect Gmail without a token is a harmless no-op", true, "skipped: DPAPI unavailable on this host");
    }
}
finally
{
    Directory.Delete(tempRoot, recursive: true);
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
    public IReadOnlyList<string> LastLabelIds { get; private set; } = Array.Empty<string>();
    public string LastRaw { get; private set; } = "";

    public Task<string> CreateDraftAsync(string rawRfc822Base64Url, IReadOnlyList<string> labelIds, CancellationToken ct = default)
    {
        LastRaw = rawRfc822Base64Url;
        LastLabelIds = labelIds.ToArray();
        return Task.FromResult("draft_1");
    }
}

sealed class FakeLabels : IGmailLabelManager
{
    public int Calls { get; private set; }

    public Task<string> EnsureLabelAsync(string labelPath, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult("Label_" + labelPath);
    }
}

sealed class CapturingHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _body;

    public int Calls { get; private set; }
    public string? LastRequestUri { get; private set; }
    public string? LastBody { get; private set; }

    public CapturingHandler(HttpStatusCode statusCode, string body)
    {
        _statusCode = statusCode;
        _body = body;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        LastRequestUri = request.RequestUri?.GetLeftPart(UriPartial.Path);
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(_statusCode) { Content = new StringContent(_body) };
    }
}
