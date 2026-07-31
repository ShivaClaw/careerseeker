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
    bool RequireByok = false,
    bool RequireServiceHost = false,
    string? HostControlDirectory = null,
    string? HostLogDirectory = null);

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
        checks.Add(CheckBraveSearch(options.EnvFilePath));
        if (options.RequireServiceHost)
        {
            checks.Add(await CheckHostPathsAsync(
                options.HostControlDirectory,
                options.HostLogDirectory,
                ct).ConfigureAwait(false));
            checks.Add(CheckSingleInstanceRail());
        }
        return new StartupDoctorReport(checks);
    }

    private static async Task<StartupCheck> CheckHostPathsAsync(
        string? controlDirectory,
        string? logDirectory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(controlDirectory) || string.IsNullOrWhiteSpace(logDirectory))
            return new StartupCheck("service_host_paths", false, "control and log directories are required");

        try
        {
            foreach (var directory in new[] { controlDirectory, logDirectory })
            {
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, ".careerseeker-host-write-test");
                await File.WriteAllTextAsync(probe, "ok", ct).ConfigureAwait(false);
                File.Delete(probe);
            }
            return new StartupCheck("service_host_paths", true, "control and log directories are writable");
        }
        catch (Exception ex)
        {
            return new StartupCheck("service_host_paths", false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static StartupCheck CheckSingleInstanceRail()
    {
        var identity = Path.Combine(Path.GetTempPath(), "careerseeker-doctor-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            if (!SingleInstanceLease.TryAcquire(identity, out var first) || first is null)
                return new StartupCheck("service_single_instance", false, "could not acquire local process lease");
            using (first)
            {
                var refusedSecond = !SingleInstanceLease.TryAcquire(identity, out var second);
                second?.Dispose();
                return new StartupCheck(
                    "service_single_instance",
                    refusedSecond,
                    refusedSecond ? "second local engine lease was refused" : "duplicate lease was unexpectedly acquired");
            }
        }
        catch (Exception ex)
        {
            return new StartupCheck("service_single_instance", false, ex.GetType().Name + ": " + ex.Message);
        }
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

    private static StartupCheck CheckBraveSearch(string envFilePath)
    {
        var names = new[]
        {
            "BRAVE_SEARCH_API_KEY",
            "BRAVE_SEARCH_API",
            "CAREERSEEKER_BRAVE_SEARCH_API_KEY",
        };

        var configuredName = names.FirstOrDefault(name =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) ||
            EnvFileHasValue(envFilePath, name));

        return new StartupCheck(
            "brave_search",
            true,
            configuredName is null ? "not configured (optional for company research)" : $"configured via {configuredName}");
    }

    private static bool EnvFileHasValue(string path, string name)
    {
        if (!File.Exists(path)) return false;

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var idx = trimmed.IndexOf('=');
            if (idx <= 0) continue;
            if (!trimmed[..idx].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            var value = trimmed[(idx + 1)..].Trim().Trim('"');
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }
}
