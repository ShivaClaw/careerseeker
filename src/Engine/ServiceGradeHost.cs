using System.Collections.Concurrent;

namespace SeekerSvc.Engine;

/// <summary>
/// Cross-process lease for one long-running engine per local database. The scheduled task also uses
/// IgnoreNew, but the mutex protects direct launches and wrapper restarts from racing that outer rail.
/// </summary>
public sealed class SingleInstanceLease : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> OwnedNames = new(StringComparer.Ordinal);
    private readonly FileStream _lockHandle;
    private readonly string _name;
    private bool _disposed;

    private SingleInstanceLease(FileStream lockHandle, string name)
    {
        _lockHandle = lockHandle;
        _name = name;
    }

    public static bool TryAcquire(string databasePath, out SingleInstanceLease? lease)
    {
        var identity = Path.GetFullPath(databasePath);
        var name = identity.ToUpperInvariant();
        if (!OwnedNames.TryAdd(name, 0))
        {
            lease = null;
            return false;
        }

        var lockPath = identity + ".engine.lock";
        try
        {
            var directory = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrWhiteSpace(directory))
                System.IO.Directory.CreateDirectory(directory);
            var handle = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
            lease = new SingleInstanceLease(handle, name);
            return true;
        }
        catch (IOException)
        {
            OwnedNames.TryRemove(name, out _);
            lease = null;
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _lockHandle.Dispose();
        OwnedNames.TryRemove(_name, out _);
        _disposed = true;
    }
}

/// <summary>
/// Local, user-owned control files used by the scheduled-task wrapper. Pause never terminates the
/// process; stop is observed by the run host and results in normal async disposal of scheduler,
/// dashboard, and SQLite resources.
/// </summary>
public sealed class EngineControlFiles
{
    public EngineControlFiles(string directory)
    {
        Directory = Path.GetFullPath(directory);
        PausePath = Path.Combine(Directory, "pause.request");
        StopPath = Path.Combine(Directory, "stop.request");
    }

    public string Directory { get; }
    public string PausePath { get; }
    public string StopPath { get; }
    public bool PauseRequested => File.Exists(PausePath);
    public bool StopRequested => File.Exists(StopPath);

    public void EnsureDirectory() => System.IO.Directory.CreateDirectory(Directory);
}
