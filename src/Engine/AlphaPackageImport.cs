using System.IO.Compression;
using System.Text.Json;
using SeekerSvc.Store;

namespace SeekerSvc.Engine;

public sealed record AlphaPackageImportOptions(
    string DbPath,
    string ArtifactDirectory,
    string JobDescriptionDirectory,
    bool Overwrite = false,
    bool IncludeDatabase = true,
    bool IncludeArtifacts = true,
    bool IncludeJobDescriptions = true);

public sealed record AlphaPackageImportResult(
    string PackagePath,
    bool AuditOk,
    int EntryCount,
    IReadOnlyList<string> Extracted);

public static class AlphaPackageImport
{
    private const int MaxManifestBytes = 64 * 1024;
    private const string ExpectedFormat = "careerseeker-alpha-package-v1";

    public static async Task<AlphaPackageImportResult> ImportAsync(
        string packagePath,
        AlphaPackageImportOptions options,
        CancellationToken ct = default)
    {
        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
            throw new FileNotFoundException("Alpha package not found.", fullPackagePath);

        var extracted = new List<string>();
        using (var zip = ZipFile.OpenRead(fullPackagePath))
        {
            ValidateEntries(zip);
            ValidateManifest(zip);

            if (options.IncludeDatabase)
                ExtractDatabase(zip, options.DbPath, options.Overwrite, extracted);

            if (options.IncludeArtifacts)
                ExtractDirectory(zip, "artifacts/", options.ArtifactDirectory, options.Overwrite, extracted);

            if (options.IncludeJobDescriptions)
                ExtractDirectory(zip, "job-descriptions/", options.JobDescriptionDirectory, options.Overwrite, extracted);
        }

        var auditOk = await VerifyImportedAuditAsync(options.DbPath, options.IncludeDatabase, ct)
            .ConfigureAwait(false);
        return new AlphaPackageImportResult(
            fullPackagePath,
            auditOk,
            extracted.Count,
            extracted);
    }

    private static void ValidateEntries(ZipArchive zip)
    {
        var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var databaseEntries = 0;
        foreach (var entry in zip.Entries)
        {
            var name = NormalizeEntryName(entry.FullName);
            if (string.IsNullOrWhiteSpace(name) || name.EndsWith("/", StringComparison.Ordinal)) continue;
            if (Path.IsPathFullyQualified(name) ||
                name.Contains("../", StringComparison.Ordinal) ||
                name.StartsWith("..", StringComparison.Ordinal) ||
                name.Contains(':', StringComparison.Ordinal) ||
                LooksSecretPath(name))
            {
                throw new InvalidOperationException($"Refusing unsafe alpha package entry '{entry.FullName}'.");
            }

            if (!IsSupportedEntry(name))
                throw new InvalidOperationException($"Refusing unsupported alpha package entry '{entry.FullName}'.");

            if (IsDatabaseEntry(name) && ++databaseEntries > 1)
                throw new InvalidOperationException($"Refusing ambiguous alpha package database entry '{entry.FullName}'.");

            if (!seenEntries.Add(name))
                throw new InvalidOperationException($"Refusing duplicate alpha package entry '{entry.FullName}'.");
        }
    }

    private static void ValidateManifest(ZipArchive zip)
    {
        var entry = zip.Entries.FirstOrDefault(e =>
            NormalizeEntryName(e.FullName).Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            throw new InvalidOperationException("Refusing alpha package without a manifest.json.");

        if (entry.Length > MaxManifestBytes)
            throw new InvalidOperationException("Refusing alpha package with an oversized manifest.json.");

        try
        {
            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("format", out var format) ||
                format.ValueKind != JsonValueKind.String ||
                !ExpectedFormat.Equals(format.GetString(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing alpha package with an unrecognized manifest format.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Refusing alpha package with an invalid manifest.json.", ex);
        }
    }

    private static void ExtractDatabase(
        ZipArchive zip,
        string dbPath,
        bool overwrite,
        List<string> extracted)
    {
        var entry = zip.Entries.FirstOrDefault(e =>
            NormalizeEntryName(e.FullName).StartsWith("database/", StringComparison.OrdinalIgnoreCase) &&
            NormalizeEntryName(e.FullName).EndsWith(".db", StringComparison.OrdinalIgnoreCase));
        if (entry is null) return;

        WriteEntry(entry, dbPath, overwrite);
        extracted.Add(Path.GetFullPath(dbPath));
    }

    private static void ExtractDirectory(
        ZipArchive zip,
        string entryRoot,
        string targetRoot,
        bool overwrite,
        List<string> extracted)
    {
        var fullTargetRoot = Path.GetFullPath(targetRoot);
        foreach (var entry in zip.Entries)
        {
            var name = NormalizeEntryName(entry.FullName);
            if (!name.StartsWith(entryRoot, StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("/", StringComparison.Ordinal))
                continue;

            var relative = name[entryRoot.Length..];
            var targetPath = SafeTargetPath(fullTargetRoot, relative);
            WriteEntry(entry, targetPath, overwrite);
            extracted.Add(targetPath);
        }
    }

    private static async Task<bool> VerifyImportedAuditAsync(string dbPath, bool includeDatabase, CancellationToken ct)
    {
        if (!includeDatabase || !File.Exists(dbPath)) return true;

        await using var store = SqliteSeekerStore.ForFile(dbPath);
        await store.InitializeAsync(ct).ConfigureAwait(false);
        var verification = await store.VerifyAuditAsync(ct).ConfigureAwait(false);
        return verification.Ok;
    }

    private static void WriteEntry(ZipArchiveEntry entry, string targetPath, bool overwrite)
    {
        var fullTargetPath = Path.GetFullPath(targetPath);
        var dir = Path.GetDirectoryName(fullTargetPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        if (File.Exists(fullTargetPath) && !overwrite)
            throw new IOException($"Refusing to overwrite existing import target '{fullTargetPath}'.");

        using var source = entry.Open();
        using var target = new FileStream(
            fullTargetPath,
            overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        source.CopyTo(target);
    }

    private static string SafeTargetPath(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to extract outside import root: '{relative}'.");
        return path;
    }

    private static string NormalizeEntryName(string name) =>
        name.Replace('\\', '/').TrimStart('/');

    private static bool IsSupportedEntry(string name) =>
        name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("audit.json", StringComparison.OrdinalIgnoreCase) ||
        IsDatabaseEntry(name) ||
        name.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("job-descriptions/", StringComparison.OrdinalIgnoreCase);

    private static bool IsDatabaseEntry(string name) =>
        name.StartsWith("database/", StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith(".db", StringComparison.OrdinalIgnoreCase);

    private static bool LooksSecretPath(string path)
    {
        var parts = NormalizeEntryName(path).Split('/');
        if (parts.Any(p => p.Equals("secrets", StringComparison.OrdinalIgnoreCase) ||
                           p.Equals("oauth", StringComparison.OrdinalIgnoreCase)))
            return true;

        var name = parts.LastOrDefault() ?? string.Empty;
        return name.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("key", StringComparison.OrdinalIgnoreCase);
    }
}
