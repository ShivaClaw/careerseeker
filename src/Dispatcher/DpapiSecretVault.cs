using System.Text;
using System.Text.Json;

namespace SeekerSvc.Dispatcher;

/// <summary>
/// DPAPI-backed local secret map for alpha provider keys and future small local secrets. Values are
/// encrypted as one JSON blob scoped to the current Windows user profile.
/// </summary>
public sealed class DpapiSecretVault
{
    private readonly string _path;

    public DpapiSecretVault(string path) => _path = path;

    public bool Exists => File.Exists(_path);

    public IReadOnlyDictionary<string, string> Load()
    {
        if (!File.Exists(_path)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var protectedBytes = File.ReadAllBytes(_path);
        var json = Encoding.UTF8.GetString(WindowsDpapi.Unprotect(protectedBytes));
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                     ?? new Dictionary<string, string>();
        return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    public void Save(IReadOnlyDictionary<string, string> values)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(values);
        File.WriteAllBytes(_path, WindowsDpapi.Protect(Encoding.UTF8.GetBytes(json)));
    }

    public void Delete()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
