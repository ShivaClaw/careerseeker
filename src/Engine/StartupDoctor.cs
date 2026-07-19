using SeekerSvc.Dispatcher;
using SeekerSvc.Gateway;
using SeekerSvc.Store;

namespace SeekerSvc.Engine;

public sealed record StartupDoctorOptions(
    string DbPath,
    string ArtifactDirectory,
    string? OAuthClientPath,
    string? GmailTokenVaultPath,
    string EnvFilePath,
    string KeyVaultPath,
    bool RequireGmail = false,
    bool RequireByok = false);

public sealed record StartupCheck(string Name, bool Ok, string Detail);

public sealed record StartupDoctorReport(IReadOnlyList<StartupCheck> Checks)
{
    public bool Ok => Checks.All(c => c.Ok);
}

public static class StartupDoctor
{
    public static async Task<StartupDoctorReport> RunAsync(StartupDoctorOptions options, CancellationToken ct = default)
    {
        var checks = new List<StartupCheck>();
        checks.Add(await CheckDatabaseAsync(options.DbPath, ct).ConfigureAwait(false));
        checks.Add(await CheckArtifactsAsync(options.ArtifactDirectory, ct).ConfigureAwait(false));
        checks.Add(CheckOAuthClient(options.OAuthClientPath, options.RequireGmail));
        checks.Add(CheckGmailVault(options.GmailTokenVaultPath, options.RequireGmail));
        checks.Add(CheckByok(options.EnvFilePath, options.KeyVaultPath, options.RequireByok));
        return new StartupDoctorReport(checks);
    }

    private static async Task<StartupCheck> CheckDatabaseAsync(string dbPath, CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            await using var store = SqliteSeekerStore.ForFile(dbPath);
            await store.InitializeAsync(ct).ConfigureAwait(false);
            var audit = await store.VerifyAuditAsync(ct).ConfigureAwait(false);
            return audit.Ok
                ? new StartupCheck("sqlite", true, "database opened and audit chain verified")
                : new StartupCheck("sqlite", false, $"audit chain failed at seq {audit.FirstBrokenSeq}: {audit.Reason}");
        }
        catch (Exception ex)
        {
            return new StartupCheck("sqlite", false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task<StartupCheck> CheckArtifactsAsync(string artifactDirectory, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(artifactDirectory);
            var path = Path.Combine(artifactDirectory, ".careerseeker-write-test");
            await File.WriteAllTextAsync(path, "ok", ct).ConfigureAwait(false);
            File.Delete(path);
            return new StartupCheck("artifacts", true, "artifact directory is writable");
        }
        catch (Exception ex)
        {
            return new StartupCheck("artifacts", false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static StartupCheck CheckOAuthClient(string? path, bool required)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new StartupCheck("gmail_oauth_client", !required, required ? "missing OAuth client JSON" : "not configured");

        try
        {
            _ = GoogleOAuthClient.Load(path);
            return new StartupCheck("gmail_oauth_client", true, "OAuth client JSON parsed");
        }
        catch (Exception ex)
        {
            return new StartupCheck("gmail_oauth_client", false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static StartupCheck CheckGmailVault(string? path, bool required)
    {
        var present = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        return new StartupCheck(
            "gmail_token_vault",
            present || !required,
            present ? "token vault is present" : required ? "missing Gmail token vault" : "not connected");
    }

    private static StartupCheck CheckByok(string envFilePath, string keyVaultPath, bool required)
    {
        try
        {
            var vault = new DpapiSecretVault(keyVaultPath);
            var vaulted = vault.Load();
            var source = vaulted.Count > 0
                ? new EnvironmentApiKeySource(vaulted)
                : EnvironmentApiKeySource.Load(envFilePath);
            var providers = source.ProvidersPresent();
            var hasBoth = providers.Contains("anthropic") && providers.Contains("google");
            return new StartupCheck(
                "byok_providers",
                hasBoth || !required,
                providers.Count == 0 ? required ? "missing Anthropic and Gemini keys" : "not configured" : string.Join(", ", providers));
        }
        catch (Exception ex)
        {
            return new StartupCheck("byok_providers", !required, required ? ex.GetType().Name + ": " + ex.Message : "not configured");
        }
    }
}
