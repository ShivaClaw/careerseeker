using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SeekerSvc.Dispatcher;

/// <summary>Desktop OAuth client metadata loaded from Google's downloaded JSON file.</summary>
public sealed record GoogleOAuthClient(
    string ClientId,
    string? ClientSecret,
    string AuthUri,
    string TokenUri,
    string RevokeUri)
{
    public static GoogleOAuthClient Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var client = root.TryGetProperty("installed", out var installed) ? installed :
            root.TryGetProperty("web", out var web) ? web :
            throw new InvalidOperationException("Google OAuth JSON must contain an installed or web client.");

        return new GoogleOAuthClient(
            Required(client, "client_id"),
            Optional(client, "client_secret"),
            Optional(client, "auth_uri") ?? "https://accounts.google.com/o/oauth2/auth",
            Optional(client, "token_uri") ?? "https://oauth2.googleapis.com/token",
            Optional(client, "revoke_uri") ?? "https://oauth2.googleapis.com/revoke");
    }

    private static string Required(JsonElement obj, string name) =>
        Optional(obj, name) ?? throw new InvalidOperationException($"Google OAuth JSON is missing {name}.");

    private static string? Optional(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}

/// <summary>
/// DPAPI-backed OAuth token vault. The encrypted blob is safe to keep on disk beside other local app
/// state; it can be decrypted only by the same Windows user profile.
/// </summary>
public sealed class DpapiTokenVault
{
    private readonly string _path;

    public DpapiTokenVault(string path) => _path = path;

    public OAuthToken? Load()
    {
        if (!File.Exists(_path)) return null;
        var protectedBytes = File.ReadAllBytes(_path);
        var json = Encoding.UTF8.GetString(WindowsDpapi.Unprotect(protectedBytes));
        return JsonSerializer.Deserialize<OAuthToken>(json);
    }

    public void Save(OAuthToken token)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(token);
        File.WriteAllBytes(_path, WindowsDpapi.Protect(Encoding.UTF8.GetBytes(json)));
    }

    public void Delete()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}

/// <summary>Persisted OAuth token state. Stored only through <see cref="DpapiTokenVault"/>.</summary>
public sealed record OAuthToken(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    string Scope)
{
    public bool IsFresh(TimeSpan skew) => ExpiresAtUtc > DateTimeOffset.UtcNow.Add(skew);
}

/// <summary>
/// Gmail compose OAuth source for local Windows. It refreshes DPAPI-vaulted tokens and, when allowed,
/// performs a desktop loopback authorization flow. Scope is fixed to gmail.compose.
/// </summary>
public sealed class GoogleOAuthTokenSource : IAccessTokenSource
{
    public const string GmailComposeScope = "https://www.googleapis.com/auth/gmail.compose";

    private readonly HttpClient _http;
    private readonly GoogleOAuthClient _client;
    private readonly DpapiTokenVault _vault;
    private readonly bool _allowInteractive;
    private readonly TimeSpan _freshSkew;
    private readonly Action<string>? _authorizationUrlSink;

    public GoogleOAuthTokenSource(
        HttpClient http,
        GoogleOAuthClient client,
        DpapiTokenVault vault,
        bool allowInteractive = false,
        TimeSpan? freshSkew = null,
        Action<string>? authorizationUrlSink = null)
    {
        _http = http;
        _client = client;
        _vault = vault;
        _allowInteractive = allowInteractive;
        _freshSkew = freshSkew ?? TimeSpan.FromMinutes(2);
        _authorizationUrlSink = authorizationUrlSink;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        var token = _vault.Load();
        if (token is not null && token.IsFresh(_freshSkew)) return token.AccessToken;

        if (token?.RefreshToken is not null)
        {
            token = await RefreshAsync(token.RefreshToken, token.Scope, ct).ConfigureAwait(false);
            _vault.Save(token);
            return token.AccessToken;
        }

        if (!_allowInteractive)
            throw new InvalidOperationException("No Gmail OAuth token is available. Run an interactive OAuth harness first.");

        token = await AuthorizeInteractiveAsync(ct).ConfigureAwait(false);
        _vault.Save(token);
        return token.AccessToken;
    }

    public async Task<bool> DisconnectAsync(CancellationToken ct = default)
    {
        var token = _vault.Load();
        if (token is null) return false;

        try
        {
            var tokenToRevoke = token.RefreshToken ?? token.AccessToken;
            if (!string.IsNullOrWhiteSpace(tokenToRevoke))
                await RevokeAsync(tokenToRevoke, ct).ConfigureAwait(false);
        }
        finally
        {
            _vault.Delete();
        }

        return true;
    }

    private async Task<OAuthToken> AuthorizeInteractiveAsync(CancellationToken ct)
    {
        var port = FreeTcpPort();
        var redirect = $"http://localhost:{port}/";
        var state = RandomUrlToken(32);
        var verifier = RandomUrlToken(64);
        var challenge = Base64Url.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();

        var authUrl = Query(_client.AuthUri, new Dictionary<string, string>
        {
            ["client_id"] = _client.ClientId,
            ["redirect_uri"] = redirect,
            ["response_type"] = "code",
            ["scope"] = GmailComposeScope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        });

        OpenBrowser(authUrl);
        _authorizationUrlSink?.Invoke(authUrl);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("OAuth callback was not received within 5 minutes.");
        }
        var query = ParseQuery(context.Request.Url?.Query);

        var html = "<html><body><h1>CareerSeeker OAuth complete</h1><p>You can close this tab.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
        context.Response.Close();

        if (!query.TryGetValue("state", out var returnedState) || returnedState != state)
            throw new InvalidOperationException("OAuth state mismatch.");
        if (query.TryGetValue("error", out var error))
            throw new InvalidOperationException($"OAuth failed: {error}");
        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("OAuth response did not include an authorization code.");

        return await ExchangeCodeAsync(code, redirect, verifier, ct).ConfigureAwait(false);
    }

    private async Task<OAuthToken> ExchangeCodeAsync(
        string code, string redirectUri, string verifier, CancellationToken ct)
    {
        var form = BaseTokenForm(new Dictionary<string, string>
        {
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        });
        return await TokenRequestAsync(form, null, ct).ConfigureAwait(false);
    }

    private async Task<OAuthToken> RefreshAsync(string refreshToken, string scope, CancellationToken ct)
    {
        var form = BaseTokenForm(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        });
        return await TokenRequestAsync(form, refreshToken, ct).ConfigureAwait(false) with { Scope = scope };
    }

    private async Task RevokeAsync(string token, CancellationToken ct)
    {
        using var resp = await _http.PostAsync(_client.RevokeUri,
                new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }), ct)
            .ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Google OAuth revoke failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. {CompactOAuthError(body)}");
    }

    private Dictionary<string, string> BaseTokenForm(Dictionary<string, string> form)
    {
        form["client_id"] = _client.ClientId;
        if (!string.IsNullOrWhiteSpace(_client.ClientSecret))
            form["client_secret"] = _client.ClientSecret!;
        return form;
    }

    private async Task<OAuthToken> TokenRequestAsync(
        Dictionary<string, string> form, string? fallbackRefreshToken, CancellationToken ct)
    {
        using var resp = await _http.PostAsync(_client.TokenUri, new FormUrlEncodedContent(form), ct)
            .ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Google OAuth token request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. {CompactOAuthError(json)}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var access = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Token response had no access_token.");
        var refresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : fallbackRefreshToken;
        var expires = root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds)
            ? DateTimeOffset.UtcNow.AddSeconds(seconds)
            : DateTimeOffset.UtcNow.AddHours(1);
        var scope = root.TryGetProperty("scope", out var s) ? s.GetString() ?? GmailComposeScope : GmailComposeScope;

        return new OAuthToken(access, refresh, expires, scope);
    }

    private static string CompactOAuthError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            var description = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            return string.Join(" ", new[] { error, description }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        catch (JsonException)
        {
            return json.Length <= 300 ? json : json[..300];
        }
    }

    private static string Query(string baseUrl, IReadOnlyDictionary<string, string> values)
    {
        var sep = baseUrl.Contains('?') ? "&" : "?";
        return baseUrl + sep + string.Join("&", values.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    private static Dictionary<string, string> ParseQuery(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(query)) return result;
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            var key = idx >= 0 ? pair[..idx] : pair;
            var value = idx >= 0 ? pair[(idx + 1)..] : "";
            result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        return result;
    }

    private static int FreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string RandomUrlToken(int bytes)
    {
        Span<byte> data = stackalloc byte[bytes];
        RandomNumberGenerator.Fill(data);
        return Base64Url.Encode(data.ToArray());
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not open the browser. Visit this URL manually: {url}", ex);
        }
    }
}

internal static class WindowsDpapi
{
    private const int CryptProtectUiForbidden = 0x1;

    public static byte[] Protect(byte[] data) => Transform(data, protect: true);

    public static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = BlobFrom(data);
        var output = new DataBlob();
        try
        {
            var ok = protect
                ? CryptProtectData(ref input, "CareerSeeker OAuth token", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, ref output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());

            var bytes = new byte[output.cbData];
            Marshal.Copy(output.pbData, bytes, 0, output.cbData);
            return bytes;
        }
        finally
        {
            if (input.pbData != IntPtr.Zero)
            {
                CryptographicOperations.ZeroMemory(data);
                Marshal.FreeHGlobal(input.pbData);
            }
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    private static DataBlob BlobFrom(byte[] data)
    {
        var blob = new DataBlob { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
        Marshal.Copy(data, 0, blob.pbData, data.Length);
        return blob;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        ref DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        ref DataBlob pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
