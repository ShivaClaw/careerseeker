namespace SeekerSvc.Engine;

internal sealed record FullDataDeletionPlan(
    string WorkspacePath,
    string ConfirmationPhrase);

internal sealed record FullDataDeletionResult(
    bool Removed,
    bool AlreadyAbsent,
    string WorkspacePath,
    bool TargetExistsAfter,
    string Message);

/// <summary>
/// Implements the separately confirmed local-data off-ramp. The public command can target only the
/// exact installed per-user workspace; isolated temp roots are accepted solely for EngineHarness.
/// Directory links are removed as links rather than traversed.
/// </summary>
internal static class FullDataDeletion
{
    private const string ConfirmationPrefix = "DELETE ALL CAREERSEEKER DATA AT ";
    private const string HarnessPrefix = "careerseeker-delete-harness-";

    internal static FullDataDeletionPlan PlanInstalledWorkspace() =>
        PlanWorkspace(PackagedRuntime.WorkspaceRoot);

    internal static FullDataDeletionPlan PlanWorkspace(string workspacePath)
    {
        var resolved = ResolveAllowedWorkspace(workspacePath);
        return new FullDataDeletionPlan(resolved, ConfirmationPrefix + resolved);
    }

    internal static FullDataDeletionResult Execute(
        FullDataDeletionPlan plan,
        string? confirmation)
    {
        var resolved = ResolveAllowedWorkspace(plan.WorkspacePath);
        var required = ConfirmationPrefix + resolved;
        if (!string.Equals(plan.ConfirmationPhrase, required, StringComparison.Ordinal))
            throw new InvalidOperationException("The deletion plan does not match the exact resolved workspace.");
        if (!string.Equals(confirmation, required, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Full-data deletion was not confirmed. The confirmation must exactly match the displayed path-bound phrase.");

        if (!Directory.Exists(resolved))
        {
            return new FullDataDeletionResult(
                Removed: false,
                AlreadyAbsent: true,
                resolved,
                TargetExistsAfter: false,
                $"No CareerSeeker workspace exists at '{resolved}'. Nothing was deleted.");
        }

        if ((File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The exact CareerSeeker workspace is a link or reparse point; refusing deletion.");

        MoveCurrentDirectoryOutside(resolved);
        DeleteTreeWithoutFollowingLinks(resolved);

        var remains = Directory.Exists(resolved) || File.Exists(resolved);
        if (remains)
            throw new IOException($"CareerSeeker data deletion did not remove the exact workspace '{resolved}'.");

        return new FullDataDeletionResult(
            Removed: true,
            AlreadyAbsent: false,
            resolved,
            TargetExistsAfter: false,
            $"CareerSeeker local data was removed from '{resolved}' and the path is now absent.");
    }

    private static string ResolveAllowedWorkspace(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
            throw new InvalidOperationException("A CareerSeeker workspace path is required.");

        var resolved = TrimDirectoryEnd(Path.GetFullPath(workspacePath));
        var volumeRoot = TrimDirectoryEnd(Path.GetPathRoot(resolved) ?? "");
        if (string.IsNullOrWhiteSpace(volumeRoot) || resolved.Equals(volumeRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing full-data deletion for a volume root.");

        var installed = TrimDirectoryEnd(Path.GetFullPath(PackagedRuntime.WorkspaceRoot));
        if (resolved.Equals(installed, StringComparison.OrdinalIgnoreCase))
            return resolved;

        var tempRoot = TrimDirectoryEnd(Path.GetFullPath(Path.GetTempPath()));
        var leaf = Path.GetFileName(resolved);
        if (IsAtOrBelow(resolved, tempRoot) && leaf.StartsWith(HarnessPrefix, StringComparison.Ordinal))
            return resolved;

        throw new InvalidOperationException(
            $"Refusing to delete '{resolved}'. The app command is pinned to the exact installed workspace '{installed}'.");
    }

    private static void MoveCurrentDirectoryOutside(string workspacePath)
    {
        var current = TrimDirectoryEnd(Path.GetFullPath(Environment.CurrentDirectory));
        if (!IsAtOrBelow(current, workspacePath)) return;

        var safe = TrimDirectoryEnd(Path.GetFullPath(AppContext.BaseDirectory));
        if (IsAtOrBelow(safe, workspacePath))
            safe = TrimDirectoryEnd(Path.GetFullPath(Path.GetTempPath()));
        if (IsAtOrBelow(safe, workspacePath))
            throw new InvalidOperationException("No safe working directory exists outside the deletion target.");

        Environment.CurrentDirectory = safe;
    }

    private static void DeleteTreeWithoutFollowingLinks(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    Directory.Delete(entry, recursive: false);
                else
                    DeleteTreeWithoutFollowingLinks(entry);
                continue;
            }

            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
            File.Delete(entry);
        }

        Directory.Delete(directory, recursive: false);
    }

    private static bool IsAtOrBelow(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative.Equals(".", StringComparison.Ordinal) ||
               (!Path.IsPathRooted(relative) &&
                !relative.Equals("..", StringComparison.Ordinal) &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static string TrimDirectoryEnd(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
