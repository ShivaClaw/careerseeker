using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SeekerSvc.Engine;

/// <summary>
/// Keeps mutable state outside the immutable MSIX install directory. Detection is performed through
/// Windows package identity rather than a command-line switch, so an unpacked executable cannot claim
/// that package-integrity enforcement is active.
/// </summary>
internal static class PackagedRuntime
{
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;

    internal static bool IsPackaged { get; private set; }
    internal static string? PackageFullName { get; private set; }
    internal static string WorkspaceRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CareerSeeker");
    internal static string OnboardingMarkerPath =>
        Path.Combine(Environment.CurrentDirectory, ".appdata", "onboarding.completed");

    internal static void Prepare()
    {
        PackageFullName = TryGetCurrentPackageFullName();
        IsPackaged = !string.IsNullOrWhiteSpace(PackageFullName);
        if (!IsPackaged) return;

        Directory.CreateDirectory(WorkspaceRoot);
        Environment.CurrentDirectory = WorkspaceRoot;

        // The OAuth client id is public application metadata, not a user token or secret. Copy it once
        // so all mutable configuration remains in the per-user workspace and upgrades preserve edits.
        var packagedClient = Path.Combine(AppContext.BaseDirectory, "resources", "google-client.json");
        var localClient = Path.Combine(WorkspaceRoot, "resources", "google-client.json");
        if (File.Exists(packagedClient) && !File.Exists(localClient))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localClient)!);
            File.Copy(packagedClient, localClient, overwrite: false);
        }
    }

    internal static string DefaultMode(string executableName, bool onboardingComplete, bool isPackaged)
    {
        if (isPackaged || executableName.Equals("CareerSeeker", StringComparison.OrdinalIgnoreCase))
            return onboardingComplete ? "run" : "setup";
        return executableName.Contains("setup", StringComparison.OrdinalIgnoreCase) ? "setup" : "demo";
    }

    private static string? TryGetCurrentPackageFullName()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var length = 0;
        var result = GetCurrentPackageFullName(ref length, null);
        if (result == AppModelErrorNoPackage) return null;
        if (result != ErrorInsufficientBuffer)
            throw new Win32Exception(result, "Windows package identity lookup failed.");

        var value = new StringBuilder(length);
        result = GetCurrentPackageFullName(ref length, value);
        if (result != 0)
            throw new Win32Exception(result, "Windows package identity lookup failed.");
        return value.ToString();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);
}
