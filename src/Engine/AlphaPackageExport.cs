using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SeekerSvc.Store;

namespace SeekerSvc.Engine;

public sealed record AlphaPackageOptions(
    string DbPath,
    string ArtifactDirectory,
    string JobDescriptionDirectory,
    bool IncludePayloads = false,
    bool IncludeDatabase = true,
    bool IncludeArtifacts = true,
    bool IncludeJobDescriptions = true);

public sealed record AlphaPackageResult(
    string PackagePath,
    bool AuditOk,
    int EventCount,
    int EntryCount,
    long Bytes,
    IReadOnlyList<string> Entries);

public static class AlphaPackageExport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<AlphaPackageResult> WriteAsync(
        ISeekerStore store,
        string packagePath,
        AlphaPackageOptions options,
        CancellationToken ct = default)
    {
        var verification = await store.VerifyAuditAsync(ct).ConfigureAwait(false);
        var events = await store.GetEventsAsync(ct).ConfigureAwait(false);
        var auditJson = await AuditExport.BuildJsonAsync(store, new AuditExportOptions(options.IncludePayloads), ct)
            .ConfigureAwait(false);

        var fullPackagePath = Path.GetFullPath(packagePath);
        var packageDir = Path.GetDirectoryName(fullPackagePath);
        if (!string.IsNullOrWhiteSpace(packageDir)) Directory.CreateDirectory(packageDir);
        if (File.Exists(fullPackagePath)) File.Delete(fullPackagePath);

        var entries = new List<string>();
        await using (var stream = File.Create(fullPackagePath))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
        {
            await AddTextAsync(zip, "manifest.json", ManifestJson(options, verification.Ok, events.Count), entries, ct)
                .ConfigureAwait(false);
            await AddTextAsync(zip, "audit.json", auditJson, entries, ct).ConfigureAwait(false);

            if (options.IncludeDatabase)
                AddDatabaseFiles(zip, options.DbPath, fullPackagePath, entries);

            if (options.IncludeArtifacts)
                AddDirectory(zip, options.ArtifactDirectory, "artifacts", fullPackagePath, entries);

            if (options.IncludeJobDescriptions)
                AddDirectory(zip, options.JobDescriptionDirectory, "job-descriptions", fullPackagePath, entries);
        }

        return new AlphaPackageResult(
            fullPackagePath,
            verification.Ok,
            events.Count,
            entries.Count,
            new FileInfo(fullPackagePath).Length,
            entries);
    }

    private static string ManifestJson(AlphaPackageOptions options, bool auditOk, int eventCount) =>
        JsonSerializer.Serialize(new
        {
            exportedAtUtc = DateTimeOffset.UtcNow,
            format = "careerseeker-alpha-package-v1",
            audit = new { ok = auditOk, eventCount },
            includes = new
            {
                audit = true,
                payloads = options.IncludePayloads,
                database = options.IncludeDatabase,
                artifacts = options.IncludeArtifacts,
                jobDescriptions = options.IncludeJobDescriptions,
            },
            excluded = new[]
            {
                "secrets/",
                ".appdata/secrets/",
                ".appdata/oauth/",
                "*.dpapi",
                "*token*",
                "*secret*",
                "*key*",
                "client_secret*.json",
                "symlinks/junctions",
            },
        }, JsonOptions);

    private static async Task AddTextAsync(
        ZipArchive zip,
        string entryName,
        string text,
        List<string> entries,
        CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        entries.Add(entryName);
    }

    private static void AddDatabaseFiles(
        ZipArchive zip,
        string dbPath,
        string packagePath,
        List<string> entries)
    {
        if (!File.Exists(dbPath) || SamePath(dbPath, packagePath) || LooksSecretPath(dbPath)) return;

        var snapshotPath = Path.Combine(
            Path.GetTempPath(),
            "careerseeker-alpha-db-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            BackupDatabase(dbPath, snapshotPath);
            AddFile(zip, snapshotPath, "database/" + Path.GetFileName(dbPath), entries);
        }
        finally
        {
            try { if (File.Exists(snapshotPath)) File.Delete(snapshotPath); } catch (IOException) { }
        }
    }

    private static void BackupDatabase(string sourcePath, string snapshotPath)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Pooling = false,
        }.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static void AddDirectory(
        ZipArchive zip,
        string directory,
        string entryRoot,
        string packagePath,
        List<string> entries)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;

        var root = Path.GetFullPath(directory);
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        foreach (var file in Directory.EnumerateFiles(root, "*", enumeration))
        {
            if (SamePath(file, packagePath) || LooksSecretPath(file) || IsReparsePoint(file)) continue;
            var relative = Path.GetRelativePath(root, file)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            AddFile(zip, file, entryRoot + "/" + relative, entries);
        }
    }

    private static void AddFile(ZipArchive zip, string path, string entryName, List<string> entries)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = entry.Open();
        source.CopyTo(target);
        entries.Add(entryName);
    }

    private static bool LooksSecretPath(string path)
    {
        var full = Path.GetFullPath(path);
        var parts = full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(p => p.Equals("secrets", StringComparison.OrdinalIgnoreCase) ||
                           p.Equals("oauth", StringComparison.OrdinalIgnoreCase)))
            return true;

        var name = Path.GetFileName(path);
        return name.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("key", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool SamePath(string a, string b) =>
        Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
