using System.Collections.Concurrent;

namespace SeekerSvc.Scout;

/// <summary>Result of one feed fetch. Transport/HTTP failures are values here, not exceptions.</summary>
public readonly record struct FetchOutcome(bool Ok, int Status, string? Body, string? Error);

/// <summary>
/// Fetches a board feed body from a URL. Abstracted so the orchestrator can run against the live
/// network (<see cref="HttpBoardFetcher"/>) or captured fixtures (<see cref="FixtureBoardFetcher"/>)
/// with identical behavior.
/// </summary>
public interface IBoardFetcher
{
    Task<FetchOutcome> FetchAsync(string url, CancellationToken ct = default);
}

/// <summary>
/// Per-host politeness: caps concurrent requests to a host and enforces a minimum spacing between
/// the start of consecutive requests to it. Safe for concurrent use across boards.
/// </summary>
public sealed class HostRateLimiter
{
    private readonly int _perHost;
    private readonly TimeSpan _minDelay;
    private readonly object _lock = new();
    private readonly Dictionary<string, SemaphoreSlim> _sems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _nextAllowed = new(StringComparer.OrdinalIgnoreCase);

    public HostRateLimiter(int perHostConcurrency, TimeSpan minDelay)
    {
        _perHost = Math.Max(1, perHostConcurrency);
        _minDelay = minDelay < TimeSpan.Zero ? TimeSpan.Zero : minDelay;
    }

    private SemaphoreSlim SemFor(string host)
    {
        lock (_lock)
        {
            if (!_sems.TryGetValue(host, out var s))
            {
                s = new SemaphoreSlim(_perHost, _perHost);
                _sems[host] = s;
            }
            return s;
        }
    }

    /// <summary>
    /// Acquire a slot for <paramref name="host"/>, waiting for both concurrency and min-spacing.
    /// Atomic with respect to cancellation: on throw, no slot is held.
    /// </summary>
    public async Task AcquireAsync(string host, CancellationToken ct = default)
    {
        var sem = SemFor(host);
        await sem.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            TimeSpan wait;
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                var start = now;
                if (_nextAllowed.TryGetValue(host, out var next) && next > now) start = next;
                wait = start - now;
                _nextAllowed[host] = start + _minDelay; // reserve this host's next start
            }
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct).ConfigureAwait(false);
        }
        catch
        {
            sem.Release(); // we took the slot but are bailing — give it back
            throw;
        }
    }

    public void Release(string host)
    {
        SemaphoreSlim? sem;
        lock (_lock) { _sems.TryGetValue(host, out sem); }
        sem?.Release();
    }
}

/// <summary>
/// Real HTTP fetcher. GETs JSON with a descriptive User-Agent, retries 429/5xx/transient transport
/// failures with exponential backoff + jitter (honoring <c>Retry-After</c>), bounds every request
/// with a timeout, and routes through a <see cref="HostRateLimiter"/> so we stay polite to each ATS.
/// </summary>
public sealed class HttpBoardFetcher : IBoardFetcher, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly ScoutOptions _opts;
    private readonly HostRateLimiter _limiter;

    public HttpBoardFetcher(ScoutOptions? options = null, HttpClient? http = null)
    {
        _opts = options ?? ScoutOptions.Default;
        _http = http ?? new HttpClient();
        _ownsHttp = http is null;
        if (_ownsHttp) _http.Timeout = Timeout.InfiniteTimeSpan; // we enforce timeout per-request
        _limiter = new HostRateLimiter(_opts.PerHostConcurrency, _opts.MinDelayPerHost);
    }

    public async Task<FetchOutcome> FetchAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new FetchOutcome(false, 0, null, $"Invalid URL: {url}");
        var host = uri.Host;

        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            await _limiter.AcquireAsync(host, ct).ConfigureAwait(false);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                req.Headers.TryAddWithoutValidation("User-Agent", _opts.UserAgent);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_opts.RequestTimeout);

                HttpResponseMessage resp;
                try
                {
                    resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // caller cancelled — propagate
                }
                catch (OperationCanceledException)
                {
                    if (attempt >= _opts.MaxRetries)
                        return new FetchOutcome(false, 0, null, $"Request timed out after {_opts.MaxRetries + 1} attempts");
                    await BackoffAsync(attempt, null, ct).ConfigureAwait(false);
                    continue;
                }
                catch (HttpRequestException ex)
                {
                    if (attempt >= _opts.MaxRetries)
                        return new FetchOutcome(false, 0, null, ex.Message);
                    await BackoffAsync(attempt, null, ct).ConfigureAwait(false);
                    continue;
                }

                using (resp)
                {
                    var status = (int)resp.StatusCode;
                    if (resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        return new FetchOutcome(true, status, body, null);
                    }

                    var retryable = status == 429 || status >= 500;
                    if (retryable && attempt < _opts.MaxRetries)
                    {
                        await BackoffAsync(attempt, RetryAfter(resp), ct).ConfigureAwait(false);
                        continue;
                    }
                    return new FetchOutcome(false, status, null, $"HTTP {status} {resp.ReasonPhrase}".Trim());
                }
            }
            finally
            {
                _limiter.Release(host);
            }
        }
    }

    private async Task BackoffAsync(int attempt, TimeSpan? retryAfter, CancellationToken ct)
    {
        TimeSpan delay;
        if (retryAfter is { } ra && ra > TimeSpan.Zero)
        {
            delay = ra;
        }
        else
        {
            var expMs = _opts.BaseRetryDelay.TotalMilliseconds * Math.Pow(2, attempt);
            var jitterMs = Random.Shared.Next(0, (int)Math.Max(1, _opts.BaseRetryDelay.TotalMilliseconds));
            delay = TimeSpan.FromMilliseconds(expMs + jitterMs);
        }
        await Task.Delay(delay, ct).ConfigureAwait(false);
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage resp)
    {
        var ra = resp.Headers.RetryAfter;
        if (ra is null) return null;
        if (ra.Delta is { } d) return d;
        if (ra.Date is { } when)
        {
            var diff = when - DateTimeOffset.UtcNow;
            return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
        }
        return null;
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}

/// <summary>
/// Offline fetcher backed by a URL → body map. Returns 200 with the body for a known URL and a 404
/// outcome for anything else, so tests and the demo exercise the exact same orchestration, parsing,
/// and dedup paths as the live fetcher — including board-level failure isolation.
/// </summary>
public sealed class FixtureBoardFetcher : IBoardFetcher
{
    private readonly ConcurrentDictionary<string, string> _byUrl;

    public FixtureBoardFetcher(IReadOnlyDictionary<string, string> byUrl)
        => _byUrl = new ConcurrentDictionary<string, string>(byUrl, StringComparer.Ordinal);

    public Task<FetchOutcome> FetchAsync(string url, CancellationToken ct = default)
        => Task.FromResult(_byUrl.TryGetValue(url, out var body)
            ? new FetchOutcome(true, 200, body, null)
            : new FetchOutcome(false, 404, null, $"No fixture registered for {url}"));

    /// <summary>Build a fetcher by reading each (url, file path) pair from disk.</summary>
    public static FixtureBoardFetcher FromFiles(IEnumerable<(string Url, string Path)> map)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (url, path) in map)
            dict[url] = File.ReadAllText(path);
        return new FixtureBoardFetcher(dict);
    }
}
