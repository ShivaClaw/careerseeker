# P1 evidence — pairing end to end through the live relay

**Captured:** 2026-07-23. Everything here is "ran it and saw it", per the evidence standard.

The spec's P1 exit is *"phone pairs with desktop through the real relay; both sides prove
replay rejection and signature verification; relay storage inspected to confirm
ciphertext-only."* The physical phone (camera QR scan, Android Keystore device key) is the
one part that needs a device and belongs to the device-equipped session — §4 lists what
remains. Everything else is proven below, live.

## 1. Live end-to-end round-trip (`tests/SyncLiveSmoke`)

Engine (`PairingManager` + `RelayClient` from `src/Sync`) pairs with a **simulated phone**
built from the *same* `src/Sync` primitives a device would use, through the deployed relay
at `https://relay.careerseeker.app`. Command:

```
dotnet run --project tests/SyncLiveSmoke -c Release -- https://relay.careerseeker.app
```

Output, 2026-07-23:

```
[ pairing p_goYgZzo5q1zL-rri ]
  PASS  engine bootstraps the channel (POST /create)
  PASS  phone submits completion (POST /pair)
  PASS  engine collects the completion (one-shot GET /pair)
  PASS  engine completes pairing
  PASS  both sides derived the same confirm code
  PASS  both sides derived the same directional keys
  PASS  engine rotates relay token provisional -> final
[ envelope round-trip ]
  PASS  engine pushes an e2p envelope
  PASS  phone pulls exactly one e2p envelope
  PASS  phone decrypts the delta to its plaintext
  PASS  phone pushes a signed p2e doc_edit
  PASS  engine pulls the p2e envelope
  PASS  engine accepts the signed doc_edit (signature verified)
  PASS  engine rejects the doc_edit under the wrong device key
[ replay + unpair ]
  PASS  relay refuses a duplicate seq (409)
  PASS  engine unpairs (DELETE)
  PASS  after unpair the token no longer authorizes
=== 17 passed, 0 failed ===
```

This covers **signature verification** (engine accepts the phone-signed `doc_edit`, rejects
it under a wrong device key) and **replay rejection** (relay 409 on a duplicate seq) — two
of the three named exit conditions — plus the full handshake and a bidirectional envelope
round-trip.

## 2. Cross-implementation agreement (three independent codebases)

The wire format is proven identical across all three implementations that must speak it:

| Implementation | How | Result |
| --- | --- | --- |
| Node (generator) | `docs/sync-vectors/generate.mjs` produces the vectors | 21 vectors, `--check` clean |
| C# engine | `tests/SyncHarness` runs `src/Sync` against them | 68 passed / 0 failed |
| Kotlin phone | `:core` `ProtocolVectorsTest` runs the JCA codec against the **same** vendored vectors | all pass (CI + local) |

Every derived value (ECDH secret, both directional keys, relay token, provisional token,
6-digit confirm code) reproduces byte-for-byte in all three; every invalid vector rejects
with the same error code on both receivers. A divergence fails in CI, not in the field.

## 3. Relay stores ciphertext only

Two independent proofs:

- **Live inspection.** The `pull` responses in §1 return the envelopes verbatim; their
  `ciphertext` fields are opaque AEAD output, and no plaintext field exists on the wire.
- **Storage-schema test** (`relay/test/relay.test.ts`, "stored rows contain ciphertext
  only"). Dumps the Durable Object's SQLite columns and asserts the exact list
  `[dir, seq, ts, key_id, nonce, ciphertext, size, expires_at]` — no identity column, no
  plaintext column. Any *new* column fails the test, forcing a deliberate look at whether
  it de-blinds the relay.
- **CI grep** proves `relay/src` contains no decryption primitive: the relay holds no key
  material and cannot read what it stores.

## 4. What remains for the device-equipped session

The `:core` cryptographic layer is complete and proven. Not yet built, because it needs a
physical device to verify honestly rather than a stub that passes vacuously:

- **Phone pairing UI** (`:app`): CameraX + ML Kit QR scan, the pairing screens, and the
  Ed25519→**ECDSA P-256** device key generated in the Android **Keystore** (StrongBox
  where available). The crypto these screens drive already passes against the shared
  vectors; what is missing is the camera/Keystore/Compose wiring, which a `./gradlew`
  build cannot exercise without an emulator or handset.
- **Desktop "Pair phone" page** (engine dashboard): renders the QR (`PairingManager`
  already produces the invite JSON) behind the same token/Host/Origin checks as every
  other mutating dashboard control. Deferred as engine-host integration; the manager it
  would call is done and tested.
- **A real phone pairing with a real desktop**, the last mile of spec success-criterion 1.
  The protocol, relay, engine, and phone-codec halves are all proven; this joins them on
  hardware.

None of the remaining work is a protocol or crypto question — those are settled and
verified. It is UI and host-integration that wants a device in hand.
