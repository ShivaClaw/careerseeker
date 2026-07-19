using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SeekerSvc.Gateway;

/// <summary>
/// How the user's key is provided. BYOK reads from the DPAPI vault; Managed uses our proxy key. Either
/// way the provider just needs a bearer/secret at call time — it does not know which mode it is in.
/// </summary>
public interface IApiKeySource
{
    string GetKey(string provider);
}

/// <summary>A fixed-key source (tests, single-tenant). Production reads the DPAPI vault.</summary>
public sealed class StaticKeySource : IApiKeySource
{
    private readonly IReadOnlyDictionary<string, string> _keys;
    public StaticKeySource(IReadOnlyDictionary<string, string> keys) => _keys = keys;
    public string GetKey(string provider) =>
        _keys.TryGetValue(provider, out var k) ? k : throw new InvalidOperationException($"No API key for '{provider}'.");
}

/// <summary>
/// BYOK key source for local alpha runs. It reads process environment variables plus an optional
/// KEY=value file, but never logs or exposes values. Provider ids stay stable even when vendors use
/// different environment variable names.
/// </summary>
public sealed class EnvironmentApiKeySource : IApiKeySource
{
    private readonly IReadOnlyDictionary<string, string> _keys;

    public EnvironmentApiKeySource(IReadOnlyDictionary<string, string> keys) => _keys = keys;

    public static EnvironmentApiKeySource Load(string? envFilePath = null)
    {
        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in new[] { "ANTHROPIC_API_KEY", "GEMINI_API_KEY", "GOOGLE_API_KEY" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) raw[name] = value.Trim();
        }

        if (!string.IsNullOrWhiteSpace(envFilePath) && File.Exists(envFilePath))
        {
            foreach (var line in File.ReadLines(envFilePath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                var idx = trimmed.IndexOf('=');
                if (idx <= 0) continue;
                var name = trimmed[..idx].Trim();
                var value = trimmed[(idx + 1)..].Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
                    raw[name] = value;
            }
        }

        var providerKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (raw.TryGetValue("ANTHROPIC_API_KEY", out var anthropic))
            providerKeys["anthropic"] = anthropic;
        if (raw.TryGetValue("GEMINI_API_KEY", out var gemini))
            providerKeys["google"] = gemini;
        else if (raw.TryGetValue("GOOGLE_API_KEY", out var google))
            providerKeys["google"] = google;

        return new EnvironmentApiKeySource(providerKeys);
    }

    public bool HasKey(string provider) => _keys.ContainsKey(provider);

    public IReadOnlyList<string> ProvidersPresent() => _keys.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public string GetKey(string provider) =>
        _keys.TryGetValue(provider, out var key)
            ? key
            : throw new InvalidOperationException(
                $"No BYOK API key for provider '{provider}'. Expected ANTHROPIC_API_KEY for anthropic and GEMINI_API_KEY or GOOGLE_API_KEY for google.");
}

/// <summary>
/// Anthropic Messages API provider. Request/response shapes follow the public v1/messages contract
/// (system + messages[], usage.input_tokens/output_tokens). Compile-verified here; the live HTTP path
/// is exercised in the real environment (the sandbox has no provider network access or keys).
/// </summary>
public sealed class AnthropicProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly IApiKeySource _keys;
    public string Name => "anthropic";
    public bool IsLocal => false;

    public AnthropicProvider(HttpClient http, IApiKeySource keys)
    {
        _http = http;
        _keys = keys;
    }

    public async Task<ProviderResult> CompleteAsync(ProviderCall call, CancellationToken ct = default)
    {
        // Anthropic takes system as a top-level field, not a message role.
        var system = string.Join("\n\n", call.Messages.Where(m => m.Role == "system").Select(m => m.Content));
        var turns = call.Messages.Where(m => m.Role != "system")
            .Select(m => new { role = m.Role, content = m.Content }).ToArray();

        var body = new Dictionary<string, object?>
        {
            ["model"] = call.ModelId,
            ["max_tokens"] = call.MaxOutputTokens,
            ["messages"] = turns,
        };
        if (!string.IsNullOrEmpty(system)) body["system"] = system;
        if (call.Temperature is { } t) body["temperature"] = t;

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.TryAddWithoutValidation("x-api-key", _keys.GetKey(Name));
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var text = new StringBuilder();
        foreach (var block in root.GetProperty("content").EnumerateArray())
            if (block.TryGetProperty("type", out var ty) && ty.GetString() == "text")
                text.Append(block.GetProperty("text").GetString());

        var usage = root.GetProperty("usage");
        var inTok = usage.GetProperty("input_tokens").GetInt32();
        var outTok = usage.GetProperty("output_tokens").GetInt32();
        return new ProviderResult(text.ToString(), new LlmUsage(inTok, outTok));
    }
}

/// <summary>
/// Google Gemini generateContent provider. Body uses contents[]/parts[] with systemInstruction; usage
/// from usageMetadata.promptTokenCount/candidatesTokenCount. Compile-verified here; live HTTP at
/// integration. Pricing for the default model (gemini-2.5-flash-lite, $0.10/$0.40 per 1M) was confirmed
/// current at build time and lives in the routing table, not here.
/// </summary>
public sealed class GoogleProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly IApiKeySource _keys;
    public string Name => "google";
    public bool IsLocal => false;

    public GoogleProvider(HttpClient http, IApiKeySource keys)
    {
        _http = http;
        _keys = keys;
    }

    public async Task<ProviderResult> CompleteAsync(ProviderCall call, CancellationToken ct = default)
    {
        var system = string.Join("\n\n", call.Messages.Where(m => m.Role == "system").Select(m => m.Content));
        var contents = call.Messages.Where(m => m.Role != "system").Select(m => new
        {
            role = m.Role == "assistant" ? "model" : "user",
            parts = new[] { new { text = m.Content } },
        }).ToArray();

        var genConfig = new Dictionary<string, object?> { ["maxOutputTokens"] = call.MaxOutputTokens };
        if (call.Temperature is { } t) genConfig["temperature"] = t;

        var body = new Dictionary<string, object?>
        {
            ["contents"] = contents,
            ["generationConfig"] = genConfig,
        };
        if (!string.IsNullOrEmpty(system))
            body["systemInstruction"] = new { parts = new[] { new { text = system } } };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{call.ModelId}:generateContent";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("x-goog-api-key", _keys.GetKey(Name));
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var text = new StringBuilder();
        if (root.TryGetProperty("candidates", out var cands) && cands.GetArrayLength() > 0)
        {
            var first = cands[0];
            if (first.TryGetProperty("content", out var content)
                && content.TryGetProperty("parts", out var parts)
                && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                    if (part.TryGetProperty("text", out var tx)) text.Append(tx.GetString());
            }

            if (text.Length == 0)
            {
                var finish = first.TryGetProperty("finishReason", out var fr) ? fr.GetString() : "unknown";
                throw new InvalidOperationException($"Google provider returned no text content (finishReason={finish}).");
            }
        }
        else
        {
            throw new InvalidOperationException("Google provider returned no candidates.");
        }

        int inTok = 0, outTok = 0;
        if (root.TryGetProperty("usageMetadata", out var um))
        {
            if (um.TryGetProperty("promptTokenCount", out var p)) inTok = p.GetInt32();
            if (um.TryGetProperty("candidatesTokenCount", out var c)) outTok = c.GetInt32();
        }
        return new ProviderResult(text.ToString(), new LlmUsage(inTok, outTok));
    }
}
