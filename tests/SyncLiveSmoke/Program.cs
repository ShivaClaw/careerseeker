// Live end-to-end pairing + signed-envelope round-trip against the REAL relay.
//
// This is the P1 exit proof minus the physical phone: the engine (PairingManager +
// RelayClient) pairs through relay.careerseeker.app with a simulated phone built from the
// same src/Sync primitives a device would use, exchanges a real ciphertext envelope both
// ways, signs a p2e command with a device key and has the engine verify it, proves the
// relay rejects a replayed sequence, and unpairs. Every blob on the wire is ciphertext.
//
// This is a LIVE network test and deliberately NOT part of the hermetic offline
// Verify-Alpha suite. Run it explicitly:
//   dotnet run --project tests/SyncLiveSmoke -c Release -- https://relay.careerseeker.app

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SeekerSvc.Sync;

var relayUrl = args.Length > 0 ? args[0] : "https://relay.careerseeker.app";
int passed = 0, failed = 0;
void Check(string name, bool ok, string? detail = null)
{
    if (ok) { passed++; Console.WriteLine($"  PASS  {name}"); }
    else { failed++; Console.WriteLine($"  FAIL  {name}{(detail is null ? "" : $"  -- {detail}")}"); }
}

Console.WriteLine($"=== CareerSeeker sync LIVE end-to-end smoke ===\nRelay: {relayUrl}\n");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

// ---- engine side: create an invite -------------------------------------------------
using var engine = new PairingManager(relayUrl, ttl: TimeSpan.FromMinutes(2));
var invite = engine.CreateInvite();
var pairing = engine.Pairing;
var client = new RelayClient(http, relayUrl, pairing);
Console.WriteLine($"[ pairing {pairing} ]");

// ---- engine bootstraps the relay channel with the provisional token ----------------
var provisional = engine.ProvisionalRelayToken();
Check("engine bootstraps the channel (POST /create)", await client.CreateAsync(provisional));

// ---- simulated phone: scan QR, derive, submit completion ---------------------------
using var phoneEcdh = PairingCrypto.CreateKeyPair();
using var phoneSigning = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var phonePub = PairingCrypto.ExportUncompressedPublic(phoneEcdh);
var phonePubB64u = Base64Url.Encode(phonePub);

Base64Url.TryDecode(invite.EnginePub, out var enginePub);
Base64Url.TryDecode(invite.Secret, out var secret);
var phoneShared = PairingCrypto.ComputeSharedSecret(phoneEcdh, enginePub);
var phoneKeys = PairingCrypto.Derive(new[] { phoneShared }, secret);

var deviceSigPub = ExportUncompressed(phoneSigning);
var pairAad = PairingCrypto.CompletionAad(pairing, invite.Suite, phonePubB64u);
var pairNonce = RandomNumberGenerator.GetBytes(12);
var pairPayload = JsonSerializer.SerializeToUtf8Bytes(new { device_sig_pub = Base64Url.Encode(deviceSigPub), ts = "2026-07-23T12:00:00Z" });
var pairCiphertext = EnvelopeCodec.Seal(phoneKeys.KeyPhoneToEngine, pairNonce, pairAad, pairPayload);
var completionBody = JsonSerializer.Serialize(new
{
    suite = invite.Suite, phone_pub = phonePubB64u,
    nonce = Base64Url.Encode(pairNonce), ciphertext = Base64Url.Encode(pairCiphertext),
});

// The phone posts its completion to the relay under the provisional token; the engine collects it.
using (var post = new HttpRequestMessage(HttpMethod.Post, $"{relayUrl}/v1/{pairing}/pair") { Content = new StringContent(completionBody) })
{
    post.Headers.Add("Authorization", $"Bearer {provisional}");
    var res = await http.SendAsync(post);
    Check("phone submits completion (POST /pair)", res.StatusCode == System.Net.HttpStatusCode.Created);
}

var collected = await client.TakeCompletionAsync(provisional);
Check("engine collects the completion (one-shot GET /pair)", collected is not null);

// ---- engine completes pairing ------------------------------------------------------
var paired = engine.CompletePairing(collected!, out var err);
Check("engine completes pairing", paired is not null, err);
Check("both sides derived the same confirm code", paired?.ConfirmCode == phoneKeys.ConfirmCode);
Check("both sides derived the same directional keys",
    paired is not null && paired.KeyEngineToPhone.SequenceEqual(phoneKeys.KeyEngineToPhone)
    && paired.KeyPhoneToEngine.SequenceEqual(phoneKeys.KeyPhoneToEngine));

// The engine rotates the relay token from provisional to the final ikm-derived one.
var finalToken = paired!.RelayToken;
var finalHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(finalToken))).ToLowerInvariant();
Check("engine rotates relay token provisional -> final", await client.RotateTokenAsync(provisional, finalHash));

// ---- engine -> phone: a real delta envelope ----------------------------------------
Console.WriteLine("\n[ envelope round-trip ]");
var deltaPlain = JsonSerializer.SerializeToUtf8Bytes(new { kind = "delta", body = new { counters = new { drafted = 1 } } });
var e2pEnvelope = SealEnvelope(pairing, "e2p", 1, paired.KeyEngineToPhone, deltaPlain, sig: null);
Check("engine pushes an e2p envelope", await client.PushAsync(finalToken, e2pEnvelope));

var (e2pPulled, _) = await client.PullAsync(finalToken, "e2p", 0);
Check("phone pulls exactly one e2p envelope", e2pPulled.Count == 1);
// The smoke fixes key_id="k-live" on both sides (SealEnvelope); the vectors cover revocation.
var e2pResult = ReceiveFrom(e2pPulled[0], _ => paired.KeyEngineToPhone);
Check("phone decrypts the delta to its plaintext", e2pResult is not null && e2pResult.Contains("drafted"));

// ---- engine -> phone via the shipping SyncPublisher: snapshot + delta ---------------
//
// The same builder the engine host uses: seal SyncPayloads with k_e2p and push through the
// RelayClient. A simulated phone then pulls both, opens them under k_e2p, and reconstructs the
// dashboard counters -- the P2 §2.2 acceptance, live against the real relay.
Console.WriteLine("\n[ SyncPublisher snapshot + delta round-trip ]");
{
    var liveCounters = new Counters(Discovered: 5, Acted: 2, Drafted: 2, Blocked: 0, Rejected: 1, Errors: 0, Cycles: 3);
    var liveApps = new[] { new AppSummary("app_live_1", "DRAFTED", "Northwind Labs", "Senior Platform Engineer", 82) };
    var liveJobs = new[] { new JobSummary("job_live_1", "Northwind Labs", "Senior Platform Engineer", Repost: false, InjectionFlag: false) };

    // key_id must match the phone receiver's active key ("k-live"); startSeq 1 continues the e2p
    // stream after the hand-built delta above, so the relay's per-direction monotonicity is honored.
    var publisher = new SyncPublisher(paired.KeyEngineToPhone, pairing, "k-live",
        (envJson, ct) => client.PushAsync(finalToken, envJson, ct), startSeq: 1);

    Check("publisher pushes a snapshot (seq 2)", await publisher.PublishSnapshotAsync(liveCounters, liveApps, liveJobs));
    Check("publisher pushes a delta (seq 3)", await publisher.PublishDeltaAsync(2, liveCounters, liveApps, liveJobs));

    var (published, latest) = await client.PullAsync(finalToken, "e2p", 1);
    Check("phone pulls the two new e2p envelopes", published.Count == 2 && latest == 3);

    var phoneReceiver = new EnvelopeReceiver("k-live");
    var kinds = new List<string>();
    Counters? reconstructed = null;
    foreach (var env in published.OrderBy(e => e.GetProperty("seq").GetInt64()))
    {
        var r = phoneReceiver.Receive(ToReceived(env), _ => paired.KeyEngineToPhone);
        if (!r.Accepted) { kinds.Add($"REJECTED:{r.Error?.ToWire()}"); continue; }
        kinds.Add(r.Kind!);
        using var doc = JsonDocument.Parse(r.Plaintext!);
        if (doc.RootElement.GetProperty("body").TryGetProperty("counters", out var c))
            reconstructed = JsonSerializer.Deserialize<Counters>(c.GetRawText());
    }
    Check("phone opens snapshot then delta in order", kinds.SequenceEqual(new[] { "snapshot", "delta" }));
    Check("phone reconstructs the dashboard counters from the payload",
        reconstructed is not null && reconstructed.Drafted == 2 && reconstructed.Cycles == 3 && reconstructed.Discovered == 5);
}

// ---- phone -> engine: a SIGNED doc_edit --------------------------------------------
var editPlain = JsonSerializer.SerializeToUtf8Bytes(new { kind = "doc_edit", body = new { app_id = "app_live", doc_kind = "cover_letter", base_rev = 1, new_text = "Live edit." } });
var p2eEnvelope = SealEnvelope(pairing, "p2e", 1, phoneKeys.KeyPhoneToEngine, editPlain, sig: phoneSigning);
Check("phone pushes a signed p2e doc_edit", await client.PushAsync(finalToken, p2eEnvelope));

var (p2ePulled, _) = await client.PullAsync(finalToken, "p2e", 0);
Check("engine pulls the p2e envelope", p2ePulled.Count == 1);
var engineReceiver = new EnvelopeReceiver(activeKeyId: "k-live", deviceSigPub: paired.DeviceSigPub);
var p2eResult = engineReceiver.Receive(ToReceived(p2ePulled[0]), dir => phoneKeys.KeyPhoneToEngine);
Check("engine accepts the signed doc_edit (signature verified)", p2eResult.Accepted, p2eResult.Error?.ToWire());

// A tampered device key must make the engine reject the same envelope.
using var wrongKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var engineWrongKey = new EnvelopeReceiver(activeKeyId: "k-live", deviceSigPub: ExportUncompressed(wrongKey));
// Fresh receiver, fresh seq window, same envelope:
var forged = engineWrongKey.Receive(ToReceived(p2ePulled[0]), dir => phoneKeys.KeyPhoneToEngine);
Check("engine rejects the doc_edit under the wrong device key", forged.Error == SyncError.BadSignature, forged.Error?.ToWire());

// ---- phone -> engine: the P4 §2.4 inbound path (entitlement + outcome + pull_request) ----
//
// A simulated phone pushes a signed `entitlement` whose body is the Play-signed entitlement-valid
// vector (so BOTH layers are exercised live: the envelope's device ECDSA sig AND the payload's RSA
// signature), a signed `outcome`, and an unsigned `pull_request`. The engine's InboundDispatcher pulls
// p2e, verifies, applies, and the pull_request round-trips a fresh snapshot back to the phone.
Console.WriteLine("\n[ inbound p2e: entitlement + outcome + pull_request (P4 §2.4) ]");
{
    var entVec = LoadVector("entitlement-valid");
    var entBody = entVec.GetProperty("plaintext_json").GetProperty("body");
    var originalJson = entBody.GetProperty("original_json").GetString()!;
    var signature = entBody.GetProperty("signature").GetString()!;
    var rsaPub = entVec.GetProperty("entitlement").GetProperty("rsa_pub_spki_b64").GetString()!;

    var entSvc = new EntitlementService(
        new GoogleSignedPayloadVerifier(rsaPub, "app.careerseeker.dashboard", new HashSet<string> { "pro_unlock" }),
        new MemEntitlementStore(), () => DateTimeOffset.UtcNow);
    var outcomeApplier = new LiveOutcomeApplier();
    // The republisher continues the e2p stream (highest so far is 3), so pull_request emits snapshot seq 4.
    var republishPub = new SyncPublisher(paired.KeyEngineToPhone, pairing, "k-live",
        (envJson, ct) => client.PushAsync(finalToken, envJson, ct), startSeq: 3);
    var republisher = new LiveRepublisher(republishPub,
        new Counters(Discovered: 9, Acted: 4, Drafted: 4, Blocked: 0, Rejected: 1, Errors: 0, Cycles: 5),
        new[] { new AppSummary("app_live_1", "OFFER", "Northwind Labs", "Senior Platform Engineer", 88) },
        new[] { new JobSummary("job_live_1", "Northwind Labs", "Senior Platform Engineer", Repost: false, InjectionFlag: false) });
    var dispatcher = new InboundDispatcher(
        new EnvelopeReceiver("k-live", paired.DeviceSigPub), entSvc,
        DeviceSignature.Fingerprint(paired.DeviceSigPub), _ => phoneKeys.KeyPhoneToEngine, outcomeApplier, republisher);

    var entPlain = JsonSerializer.SerializeToUtf8Bytes(new { kind = "entitlement", body = new { original_json = originalJson, signature } });
    Check("phone pushes a signed entitlement (p2e seq 2)",
        await client.PushAsync(finalToken, SealEnvelope(pairing, "p2e", 2, phoneKeys.KeyPhoneToEngine, entPlain, sig: phoneSigning)));
    var outcomePlain = JsonSerializer.SerializeToUtf8Bytes(new { kind = "outcome", body = new { app_id = "app_live_1", outcome = "interview", at = "2026-07-24T00:00:00Z" } });
    Check("phone pushes a signed outcome (p2e seq 3)",
        await client.PushAsync(finalToken, SealEnvelope(pairing, "p2e", 3, phoneKeys.KeyPhoneToEngine, outcomePlain, sig: phoneSigning)));
    var pullReqPlain = JsonSerializer.SerializeToUtf8Bytes(new { kind = "pull_request", body = new { since_seq = 0 } });
    Check("phone pushes a pull_request (p2e seq 4, unsigned)",
        await client.PushAsync(finalToken, SealEnvelope(pairing, "p2e", 4, phoneKeys.KeyPhoneToEngine, pullReqPlain, sig: null)));

    var (inbound, _) = await client.PullAsync(finalToken, "p2e", 1); // since the doc_edit at seq 1
    var dispatched = new List<InboundOutcome>();
    foreach (var env in inbound.OrderBy(e => e.GetProperty("seq").GetInt64()))
        dispatched.Add((await dispatcher.DispatchAsync(ToReceived(env))).Outcome);
    Check("engine dispatched entitlement -> outcome -> pull_request in order",
        dispatched.SequenceEqual(new[] { InboundOutcome.EntitlementApplied, InboundOutcome.OutcomeApplied, InboundOutcome.SnapshotRepublished }));
    Check("both layers verified live: Pro is entitled after the Play-signed entitlement", entSvc.IsEntitled());
    Check("the signed outcome reached the §2.5 applier seam", outcomeApplier.Bodies.Count == 1 && outcomeApplier.Bodies[0].Contains("interview"));

    var (republished, _) = await client.PullAsync(finalToken, "e2p", 3);
    Check("pull_request round-trips a fresh snapshot to the phone (e2p seq 4)", republished.Count == 1);
    var snap = new EnvelopeReceiver("k-live").Receive(ToReceived(republished[0]), _ => paired.KeyEngineToPhone);
    Check("the republished envelope opens as kind=snapshot", snap.Accepted && snap.Kind == "snapshot", snap.Error?.ToWire());
}

// ---- relay-side replay rejection ---------------------------------------------------
Console.WriteLine("\n[ replay + unpair ]");
Check("relay refuses a duplicate seq (409)", !await client.PushAsync(finalToken, e2pEnvelope));

// ---- unpair purges everything ------------------------------------------------------
Check("engine unpairs (DELETE)", await client.UnpairAsync(finalToken));
bool pullAfterUnpair;
try { await client.PullAsync(finalToken, "e2p", 0); pullAfterUnpair = false; }
catch (HttpRequestException) { pullAfterUnpair = true; } // 401 -> EnsureSuccessStatusCode throws
Check("after unpair the token no longer authorizes", pullAfterUnpair);

Console.WriteLine($"\n=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;

// ---------------------------------------------------------------- helpers

// For the live smoke both sides fix key_id="k-live"; the vectors cover key_id revocation.
string SealEnvelope(string pairing, string dir, long seq, byte[] key, byte[] plaintext, ECDsa? sig)
{
    var nonce = RandomNumberGenerator.GetBytes(12);
    var header = new EnvelopeHeader(1, pairing, dir, seq, "2026-07-23T12:00:00Z", "k-live");
    var aad = header.Aad();
    var ciphertext = EnvelopeCodec.Seal(key, nonce, aad, plaintext);
    var envelope = new Dictionary<string, object?>
    {
        ["v"] = 1, ["pairing"] = pairing, ["dir"] = dir, ["seq"] = seq,
        ["ts"] = "2026-07-23T12:00:00Z", ["key_id"] = "k-live",
        ["nonce"] = Base64Url.Encode(nonce), ["ciphertext"] = Base64Url.Encode(ciphertext),
    };
    if (sig is not null)
    {
        var input = DeviceSignature.SigInput(aad, Base64Url.Encode(nonce), ciphertext);
        var raw = sig.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256); // P1363 r||s
        envelope["sig"] = Base64Url.Encode(raw);
    }
    return JsonSerializer.Serialize(envelope);
}

static string? ReceiveFrom(JsonElement env, Func<string, byte[]> keyFor)
{
    var r = new EnvelopeReceiver("k-live").Receive(ToReceived(env), keyFor);
    return r.Accepted ? Encoding.UTF8.GetString(r.Plaintext!) : null;
}

static ReceivedEnvelope ToReceived(JsonElement e) => new(
    e.GetProperty("v").GetInt32(), e.GetProperty("pairing").GetString()!, e.GetProperty("dir").GetString()!,
    e.GetProperty("seq").GetInt64(), e.GetProperty("ts").GetString()!, e.GetProperty("key_id").GetString()!,
    e.GetProperty("nonce").GetString()!, e.GetProperty("ciphertext").GetString()!,
    e.TryGetProperty("sig", out var s) ? s.GetString() : null);

static byte[] ExportUncompressed(ECDsa key)
{
    var p = key.ExportParameters(false);
    var point = new byte[65];
    point[0] = 0x04;
    p.Q.X!.CopyTo(point, 1);
    p.Q.Y!.CopyTo(point, 33);
    return point;
}

// Load a shared vector by name from docs/sync-vectors/v1 (the same files the offline harnesses read),
// so the live entitlement body is the exact Play-signed bytes the hermetic suite pins.
static JsonElement LoadVector(string name)
{
    var dir = LocateVectorDir() ?? throw new InvalidOperationException("docs/sync-vectors/v1 not found on any parent of the run directory");
    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, name + ".json")));
    return doc.RootElement.Clone();
}

static string? LocateVectorDir()
{
    var candidates = new List<string>();
    var probe = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (probe is not null) { candidates.Add(probe.FullName); probe = probe.Parent; }
    probe = new DirectoryInfo(AppContext.BaseDirectory);
    while (probe is not null) { candidates.Add(probe.FullName); probe = probe.Parent; }
    return candidates
        .Select(root => Path.Combine(root, "docs", "sync-vectors", "v1"))
        .FirstOrDefault(path => File.Exists(Path.Combine(path, "index.json")));
}

/// <summary>In-memory entitlement flag store for the live smoke (persistence + audit are proven against the real store in EngineHarness).</summary>
sealed class MemEntitlementStore : IEntitlementStateStore
{
    private EntitlementState? _state;
    public EntitlementState? Load() => _state;
    public void Save(EntitlementState state) => _state = state;
    public void AuditApplied(string productId, string orderId, string deviceFingerprint, bool acknowledged) { }
}

/// <summary>Records the outcome bodies handed to the §2.5 seam during the live round-trip.</summary>
sealed class LiveOutcomeApplier : IOutcomeApplier
{
    public readonly List<string> Bodies = new();
    public Task<OutcomeVerdict> ApplyAsync(string outcomeBodyJson, string deviceFingerprint, CancellationToken ct = default)
    { Bodies.Add(outcomeBodyJson); return Task.FromResult(OutcomeVerdict.Ok); }
}

/// <summary>Re-publishes a fresh snapshot through the shipping SyncPublisher when a pull_request arrives.</summary>
sealed class LiveRepublisher(SyncPublisher publisher, Counters counters, AppSummary[] apps, JobSummary[] jobs) : ISnapshotRepublisher
{
    public Task RepublishSnapshotAsync(long sinceSeq, CancellationToken ct = default)
        => publisher.PublishSnapshotAsync(counters, apps, jobs, ct);
}
