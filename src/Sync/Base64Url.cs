namespace SeekerSvc.Sync;

/// <summary>
/// Strict, unpadded base64url (RFC 4648 §5). Deliberately rejects padded and
/// standard-alphabet input: accepting both spellings would mean the shared test vectors
/// no longer pin one encoding, and two implementations could disagree about what a
/// given envelope even says.
/// </summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string? s, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrEmpty(s)) return false;
        if (s.Contains('=') || s.Contains('+') || s.Contains('/')) return false;

        var b64 = s.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
            case 1: return false;
        }

        try { bytes = Convert.FromBase64String(b64); return true; }
        catch (FormatException) { return false; }
    }
}
