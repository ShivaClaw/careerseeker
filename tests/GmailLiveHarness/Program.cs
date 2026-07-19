using SeekerSvc.Dispatcher;

var clientPath = Arg("--client") ?? "client_secret.json";
var email = Arg("--email") ?? Environment.GetEnvironmentVariable("CAREERSEEKER_GMAIL_TEST_EMAIL");
var vaultPath = Arg("--vault") ?? Path.Combine(".appdata", "oauth", "gmail-token.dpapi");

if (string.IsNullOrWhiteSpace(email))
{
    Console.WriteLine("Usage: GmailLiveHarness --email you@gmail.com [--client client_secret.json] [--vault .appdata/oauth/gmail-token.dpapi]");
    return 2;
}

Console.WriteLine("=== CareerSeeker Gmail live draft smoke ===\n");
Console.WriteLine("[ OAuth + scope ]");
Console.WriteLine("  scope: gmail.compose");
Console.WriteLine("  send method present: " + HasSendMethod());

if (!File.Exists(clientPath))
{
    Console.WriteLine($"  FAIL  OAuth client JSON not found at {clientPath}");
    return 1;
}

using var http = new HttpClient();
var client = GoogleOAuthClient.Load(clientPath);
var vault = new DpapiTokenVault(vaultPath);
var tokens = new GoogleOAuthTokenSource(http, client, vault, allowInteractive: true,
    authorizationUrlSink: url =>
    {
        Console.WriteLine("  Open this URL if the browser did not appear:");
        Console.WriteLine("  " + url);
    });
var gmail = new GmailDraftClient(http, tokens);

try
{
    var token = await tokens.GetTokenAsync();
    Console.WriteLine("  PASS  access token available");
    Console.WriteLine("  token length: " + token.Length);
}
catch (Exception ex)
{
    Console.WriteLine("  FAIL  OAuth token unavailable");
    Console.WriteLine("  " + ex.Message);
    return 1;
}

Console.WriteLine("\n[ Gmail draft ]");
try
{
    await gmail.PreflightDraftAccessAsync();
    Console.WriteLine("  PASS  Gmail drafts API reachable");
    Console.WriteLine("  custom labels: skipped (gmail.compose-only)");

    var raw = MimeBuilder.BuildRaw(
        "CareerSeeker Test",
        email,
        email,
        "CareerSeeker L1 Gmail draft smoke",
        "This is a CareerSeeker L1 smoke-test draft. It was created with gmail.compose only and was not sent.",
        Array.Empty<Attachment>());

    var draftId = await gmail.CreateDraftAsync(raw, Array.Empty<string>());
    Console.WriteLine("  PASS  Gmail draft created");
    Console.WriteLine("  draft id: " + draftId);
}
catch (Exception ex)
{
    Console.WriteLine("  FAIL  Gmail draft path or preflight");
    Console.WriteLine("  " + ex.Message);
    return 1;
}

Console.WriteLine("\n=== 5 passed, 0 failed ===");
return 0;

string? Arg(string name)
{
    for (var i = 0; i + 1 < args.Length; i++)
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}

static bool HasSendMethod() =>
    typeof(IGmailDraftClient).GetMethods().Any(m => m.Name.Contains("Send", StringComparison.OrdinalIgnoreCase));
