using System.Text.Json;
using System.Text.RegularExpressions;

namespace SeekerSvc.Sync;

/// <summary>
/// Parses an envelope off the wire into a <see cref="ReceivedEnvelope"/>, strictly.
///
/// This is the C# counterpart of the phone's `core/.../EnvelopeJson.kt`, and it exists to
/// close a rule that was normative in prose and enforced on one side only. §3 ends with:
///
/// <code>
/// Other unknown top-level fields MUST be rejected, not ignored. A permissive parser here
/// is how a future version's field silently becomes an injection point.
/// </code>
///
/// Nothing in `src/Sync` enforced that, because there was no inbound wire parser at all:
/// <see cref="ReceivedEnvelope"/> is a record that callers built field-by-field from
/// already-parsed JSON, reading the nine names they wanted and dropping everything else.
/// An envelope carrying a tenth field therefore decrypted and was **accepted** by the
/// engine while the phone rejected it — the two implementations disagreed about what a
/// well-formed envelope is, which is the disagreement the shared vectors exist to catch and
/// could not, because no vector could be added while the engine had nowhere to reject one
/// (recorded as B-6 in the android repo, and as PQ-A2-3).
///
/// Everything here happens **before** any crypto. Structural failures are reported as
/// <see cref="SyncError.DecryptFailed"/>, per §3's structural-rejection paragraph: v1
/// deliberately does not add a `malformed` code, because a distinct code would be a new
/// observable and §7.2 requires that a rejection communicate no more than "this envelope is
/// not acceptable". The phone reports the same code by the same reasoning (PQ-A2-2).
///
/// **Check order matches the phone deliberately**, including the consequence: the unknown-field
/// check runs before the version check, so a v2 sender that bumps `v` *and* adds a field is
/// told `decrypt_failed` rather than `version_unsupported` and cannot learn that the version
/// is the problem. That is a diagnosability cost, not a safety one, it is recorded as PQ-ER-1,
/// and matching the phone is the point: an engine that answered a different code than the
/// phone for the same bytes would be the cross-implementation drift this parser was written
/// to remove.
/// </summary>
public static class EnvelopeJson
{
    /// <summary>Exactly the fields §3 defines. `sig` is the only optional one.</summary>
    public static readonly IReadOnlySet<string> KnownFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "v", "pairing", "dir", "seq", "ts", "key_id", "nonce", "ciphertext", "sig",
    };

    /// <summary>Pairing ids are `p_` + 16 base64url chars (§3). Opaque, never derived from anything personal.</summary>
    private static readonly Regex PairingId = new("^p_[A-Za-z0-9_-]{16}$", RegexOptions.Compiled);

    public static bool IsValidPairingId(string value) => PairingId.IsMatch(value);

    public sealed record ParseResult(ReceivedEnvelope? Envelope, SyncError? Error)
    {
        public bool Ok => Envelope is not null;
    }

    public static ParseResult Parse(string wire)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(wire); }
        catch (JsonException) { return Fail(); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail();

            // Unknown-field rejection comes first: if the sender is speaking a dialect this
            // receiver does not know, nothing else it says should be interpreted.
            foreach (var prop in root.EnumerateObject())
                if (!KnownFields.Contains(prop.Name)) return Fail();

            if (IntField(root, "v") is not { } v) return Fail();
            if (StringField(root, "pairing") is not { } pairing) return Fail();
            if (StringField(root, "dir") is not { } dir) return Fail();
            if (LongField(root, "seq") is not { } seq) return Fail();
            if (StringField(root, "ts") is not { } ts) return Fail();
            if (StringField(root, "key_id") is not { } keyId) return Fail();
            if (StringField(root, "nonce") is not { } nonce) return Fail();
            if (StringField(root, "ciphertext") is not { } ciphertext) return Fail();

            // Absent and JSON null are both "no signature"; a present-but-non-string sig is
            // malformed rather than absent, so it must not silently degrade into "unsigned"
            // (that would turn a broken signature into a missing one and change which check
            // fires — bad_signature is reported after decryption, decrypt_failed before it).
            string? sig;
            if (!root.TryGetProperty("sig", out var sigEl)) sig = null;
            else if (sigEl.ValueKind == JsonValueKind.Null) sig = null;
            else if (sigEl.ValueKind == JsonValueKind.String) sig = sigEl.GetString();
            else return Fail();

            // Shape check only -- it costs nothing and keeps an obviously-wrong pairing id from
            // reaching the AAD, where it would fail as a confusing decrypt error instead.
            if (!IsValidPairingId(pairing)) return Fail();

            return new ParseResult(
                new ReceivedEnvelope(v, pairing, dir, seq, ts, keyId, nonce, ciphertext, sig), null);
        }
    }

    private static ParseResult Fail() => new(null, SyncError.DecryptFailed);

    private static string? StringField(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static long? LongField(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
            && el.TryGetInt64(out var value)
            ? value
            : null;

    private static int? IntField(JsonElement root, string name) =>
        LongField(root, name) is { } l && l >= int.MinValue && l <= int.MaxValue ? (int)l : null;
}
