# CareerSeeker Sync Protocol v1

The wire format between the Windows engine, the blind relay, and the Android app.

**Status:** v1 draft, P0 of the Android program. **Normative** — this document, not any
implementation, defines the wire format. Where an older document disagrees, this one wins
and the older one gets amended (see §9).

**Audience:** the C# `SyncPublisher`/`SyncHarness` in this repo, the Kotlin `:core` module
in `ShivaClaw/careerseeker-android`, and the relay Worker in `relay/`.

Requirement keywords **MUST**, **MUST NOT**, **SHOULD**, **MAY** are used in the RFC 2119
sense.

---

## 1. The one property this protocol exists to preserve

**The relay learns nothing but metadata.** It sees a pairing id, a direction, a sequence
number, a size, and a timestamp. It never sees plaintext, never holds a key, and cannot
forge a message that either endpoint will accept.

Everything below follows from that. If a change to this protocol would let the relay read
or forge content, the change is wrong regardless of what it buys.

A second property matters nearly as much: **the engine is authoritative.** The phone
proposes; the engine disposes. No envelope from the phone causes an irreversible action on
its own, and no envelope from anyone creates a path to sending email — see §8.

---

## 2. Transport

| Route | Method | Purpose |
| --- | --- | --- |
| `/v1/{pairing}/create` | POST | Engine bootstraps the pairing channel (§5.2.1) |
| `/v1/{pairing}/pair` | POST | Phone submits the pairing completion (§5.2.2) |
| `/v1/{pairing}/pair` | GET | Engine collects the completion; one-shot (§5.2.2) |
| `/v1/{pairing}/push` | POST | Append one envelope to the recipient's queue |
| `/v1/{pairing}/pull?since={seq}&dir={dir}` | GET | Fetch envelopes for direction `dir` with `seq > since` |
| `/v1/{pairing}/live` | WSS | Live fan-out while a client is connected |
| `/v1/{pairing}` | DELETE | Unpair — purge the Durable Object and all queued envelopes |
| `/v1/health` | GET | Liveness. Returns no pairing information. |

All routes require `Authorization: Bearer <relay_token>` except `/v1/health`. The token
authenticates **the pairing, not a person** — the relay has no concept of a user, an
account, or an email address, and MUST NOT acquire one. Both endpoints derive the token
from the pairing secret (§5.2.3), so the relay never mints or distributes a credential;
it only recognises one. The relay MUST store a **hash** of the token and compare in
constant time — a relay storage dump must not yield usable bearer tokens.

Transport is HTTPS/WSS only. Clients MUST reject cleartext. Envelopes are JSON, UTF-8.

**Retention.** The relay MUST purge any envelope older than the configured TTL, which MUST
NOT exceed 30 days (`CareerSeeker-Spec.md` §8.3). The relay MUST NOT log envelope bodies.

---

## 3. Envelope

The envelope is the only structure the relay parses.

```json
{
  "v": 1,
  "pairing": "p_7Fq2mXk9LtVbN3wR",
  "dir": "e2p",
  "seq": 48211,
  "ts": "2026-06-11T14:02:11Z",
  "key_id": "k-2026-06-01",
  "nonce": "3q2-796tvu_erb7v",
  "ciphertext": "…"
}
```

| Field | Type | Notes |
| --- | --- | --- |
| `v` | int | Protocol version. MUST be `1`. See §7. |
| `pairing` | string | Pairing id, `p_` + 16 base64url chars. Opaque; not derived from anything personal. |
| `dir` | string | `e2p` (engine→phone) or `p2e` (phone→engine). |
| `seq` | int | Per-direction monotonic counter, starts at 1. See §6. |
| `ts` | string | RFC 3339 UTC, sender's clock. **Advisory only** — never used for security decisions (§6.3). |
| `key_id` | string | Which derived key encrypted this. See §5.3. |
| `nonce` | string | base64url, 12 bytes, unpadded. Fresh CSPRNG value per envelope. |
| `ciphertext` | string | base64url, unpadded. AES-256-GCM output with the 16-byte tag appended. |
| `sig` | string | *Optional; see below.* base64url ECDSA-P256-SHA256 signature (§5.4). |

All base64url values are **unpadded** (RFC 4648 §5, no `=`). Decoders MUST reject padded
input rather than accepting both, so the vectors mean one thing.

`sig` is **required** on `p2e` envelopes whose payload kind is state-changing
(`doc_edit`, `outcome`, `entitlement`) and **forbidden** elsewhere — a receiver MUST
reject an `e2p` envelope carrying `sig`, and MUST reject a state-changing `p2e` envelope
lacking it (`bad_signature`, checked *after* decryption reveals the kind). Amended in P1;
the P0 draft carried the signature inside the payload body, which required canonical JSON
to verify — see §5.4 for why it moved.

Other unknown top-level fields MUST be rejected, not ignored. A permissive parser here is
how a future version's field silently becomes an injection point.

### 3.1 Size limits

An envelope MUST NOT exceed **1 MiB** total. The relay MUST reject larger with HTTP 413.
Document payloads that would exceed this are chunked at the payload layer (§4.4), not by
splitting ciphertext — a partial AEAD frame is not decryptable and must never be on the
wire.

---

## 4. Payloads

### 4.1 Additional authenticated data

The header is authenticated but not encrypted, so the relay cannot tamper with routing
fields without breaking decryption. The AAD is a byte string built from the envelope
header in **exactly** this form — one line, ASCII, no whitespace, fields in this order:

```
v=1|pairing=p_7Fq2mXk9LtVbN3wR|dir=e2p|seq=48211|ts=2026-06-11T14:02:11Z|key_id=k-2026-06-01
```

A deterministic ASCII string is used rather than canonical JSON deliberately: JSON
canonicalization (key order, number formatting, Unicode escaping) is a well-known source
of cross-language mismatch, and this protocol has two independent implementations that
must agree byte-for-byte.

`nonce` and `ciphertext` are **not** in the AAD — the nonce is an explicit AEAD input and
the ciphertext is the thing being authenticated.

### 4.2 Plaintext structure

```json
{ "kind": "delta", "body": { … } }
```

`kind` MUST be one of the kinds in §4.3. A receiver that does not recognise `kind` MUST
reject the envelope with `unknown_kind` (§7.2) and MUST NOT act on `body`.

### 4.3 Payload kinds in v1

Engine → phone:

| Kind | Body |
| --- | --- |
| `snapshot` | Full dashboard state. Sent on pairing and on engine start. |
| `delta` | Applications/jobs/counters changed since a given seq. |
| `doc` | One document: `{app_id, doc_kind, rev, text, verified}`. `doc_kind` ∈ `draft_email` \| `cover_letter` \| `resume_text`. |
| `evidence` | `{audit_ok, first_broken_seq?, event_count, events:[{seq, ts, actor, kind, entity, entity_id}]}` — the engine's audit-chain verdict plus recent event metadata. Never full event payload bodies. |
| `heartbeat` | `{ts, cycle, counters}`. Drives the app's "last seen" indicator. |
| `conflict` | Rejection of a `doc_edit`: `{app_id, doc_kind, base_rev, current_rev}`. |
| `entitlement_ack` | Engine confirms a Pro voucher was accepted. |
| `error` | §7.2. |

Phone → engine:

| Kind | Body |
| --- | --- |
| `doc_edit` | `{app_id, doc_kind, base_rev, new_text, device_sig}`. See §5.4 and §8. |
| `outcome` | Pro: `{app_id, outcome, at}`. `outcome` ∈ `sent` \| `replied` \| `interview` \| `offer` \| `rejected`. |
| `entitlement` | `{voucher}` — the signed Pro voucher from the entitlement Worker. |
| `pull_request` | `{since_seq}` — ask the engine to re-publish from a sequence point. |
| `error` | §7.2. |

**Reserved, not implemented in v1.** These names are claimed so a future L2 cannot collide
with v1 traffic, and a v1 receiver MUST reject them as `unknown_kind`:
`gate_request`, `gate_resolve`, `kill`, `config_change`, `lesson_proposal`, `metric`,
`state_change`. This is deliberate — `Android-Dashboard-Pro-Spec` §10 makes L2 gate
approval from the phone a non-goal for v1 while reserving the envelope kinds.

`kill` is reserved rather than shipped even though `CareerSeeker-Spec.md` §6.3 describes a
kill switch: a remote stop command is an L2 control-plane action, and shipping it in v1
would mean the phone can change engine behaviour before the signing and audit story has
been through an external audit.

### 4.4 Chunking

A `doc` or `doc_edit` body whose text would exceed the envelope limit is split into
`{chunk_ix, chunk_of, chunk_id}` parts. The receiver buffers by `chunk_id` and MUST
discard an incomplete set after 5 minutes. Each chunk is a complete, independently
authenticated envelope — chunking happens above the AEAD layer, never inside it.

---

## 5. Cryptography

### 5.1 Cipher: AES-256-GCM

**Decided 2026-07-22 (Gate P0-CIPHER).** 256-bit key, 96-bit nonce, 128-bit tag.

`CareerSeeker-Spec.md` §7.2 originally specified XChaCha20-Poly1305. That is amended
(§9), for a concrete reason: .NET's `System.Security.Cryptography` implements AES-GCM
natively via `AesGcm` and does **not** implement XChaCha20. Honoring the original text
would mean adding a third-party crypto library to the engine's security-critical path, in
a project that has deliberately stayed dependency-light. Google Tink supports AES-256-GCM
on the Android side, so neither implementation takes on a new dependency.

The tradeoff accepted: a 96-bit nonce is small enough that random generation has a
birthday bound. At one envelope per second continuously it would take on the order of
10^14 years to reach a 2^-32 collision probability, which is far outside this product's
lifetime. Nonces are CSPRNG-generated per envelope and MUST NOT be counter-derived —
a counter that resets after a crash is a worse failure than the birthday bound.

### 5.2 Key agreement

**Amended in P1 (gates P1-CURVE, 2026-07-23).** The P0 draft specified X25519, which
neither platform can build without new dependencies or degraded key storage (see the P1
runbook and `Post-Quantum-Posture.md` in the program repo). v1 uses **ECDH P-256**, and
the handshake is *suite-versioned* so the post-quantum hybrid is a suite bump rather than
a breaking change.

The QR rendered on the desktop encodes:

```json
{ "v": 1,
  "suite": "p256-hkdf-sha256",
  "pairing": "p_7Fq2mXk9LtVbN3wR",
  "engine_pub": "<base64url uncompressed P-256 point, 65 bytes>",
  "relay": "https://relay.careerseeker.app",
  "secret": "<base64url, 32 bytes, single-use, 60s TTL>" }
```

`suite` values: `p256-hkdf-sha256` (v1); `p256+mlkem768-hkdf-sha256` (reserved for the
hybrid migration — under it, the QR additionally carries the ML-KEM encapsulation key, and
`ikm` below becomes a concatenation of both shared secrets). A phone that does not
recognise `suite` MUST refuse to pair, showing the version mismatch — never silently fall
back. QR payload budget is checked against the hybrid suite's sizes now: ML-KEM-768's
1184-byte key fits comfortably in a version-40 QR alongside these fields.

Both sides derive, with `ss = ECDH-P256(own_priv, peer_pub)` (the 32-byte X coordinate):

```
ikm          = concat(ss)                 // one element in v1; hybrid appends mlkem_ss
salt         = secret                     // the QR's one-time secret
k_e2p        = HKDF-SHA256(ikm, salt, info="careerseeker/v1/e2p",         32)
k_p2e        = HKDF-SHA256(ikm, salt, info="careerseeker/v1/p2e",         32)
relay_token  = b64u( HKDF-SHA256(ikm, salt, info="careerseeker/v1/relay-token", 32) )
confirm      = BE_uint32( HKDF-SHA256(ikm, salt, info="careerseeker/v1/confirm", 4) )
               mod 1_000_000, rendered as 6 digits, zero-padded
```

`ikm` is **always the concatenation function over the suite's shared secrets**, even while
there is only one. Deriving from the raw ECDH output directly would make the hybrid
migration a breaking change for every paired device; deriving through `concat` makes it a
suite bump. This line is deliberate and load-bearing — do not "simplify" it away.

Two directional keys, so a captured envelope cannot be replayed back at its sender. The
6-digit `confirm` code is displayed on both screens and the user confirms they match —
a shoulder-surfer who photographs the QR cannot complete pairing: the secret is burned on
first use (engine-enforced), and the confirmation step catches a raced completion.

**Keys never touch the relay.** The relay sees the pairing id, the token hash, and — in
the completion message only — the phone's *public* ECDH key. Public keys are not secrets;
everything secret rides inside ciphertext or never leaves a device.

#### 5.2.1 Channel bootstrap (engine → relay)

Before rendering the QR, the engine calls `POST /v1/{pairing}/create` with
`Authorization: Bearer <relay_token>`. The relay instantiates the Durable Object, stores
`SHA-256(relay_token)`, and answers 201 (or 409 if the pairing id exists). From that point
every route on the pairing requires the same bearer, compared in constant time against
the stored hash. The engine can do this because it derives `relay_token` before the phone
exists — the derivation needs only `ikm`… which needs the phone's key. **It does not:**
the engine derives a *provisional* token from
`HKDF-SHA256(secret, salt="careerseeker/v1/bootstrap", info="careerseeker/v1/relay-token", 32)`
— keyed on the one-time secret alone — and both sides replace it with the `ikm`-derived
token in their first authenticated exchange after completion. The provisional token is
exactly as secret as the QR itself, lives at most 60 seconds beyond it, and a relay
compromise during that window yields a token whose channel has never carried an envelope.

#### 5.2.2 Pairing completion (phone → relay → engine)

The phone, having scanned the QR and derived keys:

`POST /v1/{pairing}/pair` (bearer: provisional token) with body:

```json
{ "suite": "p256-hkdf-sha256",
  "phone_pub": "<base64url uncompressed P-256 point>",
  "nonce": "<base64url 12 bytes>",
  "ciphertext": "<base64url>" }
```

where `ciphertext = AES-256-GCM(k_p2e, nonce, aad, payload)` with
`aad = "careerseeker/v1/pair|" + pairing + "|" + suite + "|" + phone_pub` and
`payload = {"device_sig_pub": "<base64url uncompressed P-256 point>", "ts": "..."}`.

`phone_pub` must travel in clear — the engine cannot derive `k_p2e` without it — but it is
bound into the AAD, so the relay cannot substitute its own key without breaking the tag:
a swapped `phone_pub` changes both the derived key *and* the AAD, and decryption fails
either way. The **device signing public key travels only inside the ciphertext**, per the
original spec's intent: the relay never learns which signing key belongs to a pairing.

The relay stores the completion (one per pairing; second POST → 409) for the engine to
collect via `GET /v1/{pairing}/pair`, which is **one-shot**: the relay deletes it on
read. The engine then: derives `ikm` from `phone_pub` → verifies the ciphertext →
extracts `device_sig_pub` → burns the one-time secret → displays `confirm` and waits for
the user's match → records the pairing (suite included) in the audit chain.

The engine MUST accept only the **first** valid completion and only within the secret's
TTL. `error` code for a losing or late completion: `pairing_unknown`.

#### 5.2.3 Relay token summary

| Phase | Token | Derivation |
| --- | --- | --- |
| Bootstrap → completion collected | provisional | HKDF over the one-time secret (§5.2.1) |
| Paired, steady state | final | HKDF over `ikm` (§5.2) |

The engine rotates the relay-side hash from provisional to final by calling
`POST /v1/{pairing}/create` again with the old bearer and the new token hash in the body
(`{"rotate_to": "<SHA-256 hex of final token>"}`). Rotation is idempotent and one-way.

### 5.3 Key ids and rotation

`key_id` identifies which derived key was used, so a receiver can reject envelopes
encrypted under a superseded pairing without attempting decryption. Format:
`k-<YYYY-MM-DD>` of the pairing date, plus `-<n>` if more than one pairing occurs that day.

v1 supports **exactly one active pairing** (multi-device is a non-goal). Re-pairing
generates new keys and a new `key_id`; the engine MUST reject all envelopes bearing the
old `key_id` from that moment, and unpair MUST wipe the phone's replica.

A receiver MUST reject `key_id != active_key_id` with `key_unknown` **before attempting
decryption**, and MUST NOT rely on the AEAD tag to catch it. Those are different failures:
a superseded pairing whose derived key happens to still decrypt is precisely the case a
tag check cannot see, and treating "it decrypted" as "it was authorized" is how a revoked
device keeps working. Revocation is an explicit check, not a side effect of cryptography.

### 5.4 Device signing key

**Amended in P1 (gate P1-CURVE).** The P0 draft said Ed25519 in the Android Keystore —
impossible below API 33, and this program's minSdk is 26. The device key is **ECDSA
P-256**, generated in the Android Keystore (hardware-backed from API 23, StrongBox from
28), never exportable, and its public half travels only inside the encrypted pairing
completion (§5.2.2).

Every phone-originated envelope whose kind is state-changing (`doc_edit`, `outcome`,
`entitlement`) carries the top-level `sig` field (§3): **ECDSA-P256-SHA256** over the
ASCII string

```
careerseeker/v1/cmd|<AAD string per §4.1>|<nonce b64u>|<sha256-hex of the raw ciphertext bytes>
```

The signature moved from *inside the payload body* (P0 draft) to *over the envelope*,
deliberately. Signing body fields requires both implementations to serialise JSON
identically — the canonicalization trap §4.1 exists to avoid — whereas the AAD string,
the nonce, and the ciphertext bytes are already exact wire artifacts both sides possess.
The signature therefore binds the *entire* envelope: header (via AAD), sequence number
(anti-replay for the signature itself), and content (via the ciphertext hash), with
nothing left to canonicalise. Signature encoding: base64url over the raw 64-byte `r||s`
form (not DER), fixed-width, big-endian.

The engine MUST verify `sig` against the pairing's `device_sig_pub` before applying
anything, and MUST record the signature and the key's fingerprint
(`SHA-256(uncompressed point)`, first 16 hex chars) in its hash-chained audit log. This
extends the project's "nobody is ever blind" property to remote actions: the audit trail
can prove *a specific paired device* asked for a change, not merely that a change
happened.

Encryption alone would not give this. The AEAD proves the sender held the shared key; the
signature proves which device, non-repudiably, in a form that survives in the audit log
after decryption. A future L2 — phone-approved gate decisions — inherits this mechanism
unchanged, which is why it is built now rather than when L2 needs it.

---

## 6. Ordering and replay

### 6.1 Sequence numbers

Each direction has an independent counter starting at 1, incremented per envelope, and
persisted by the sender across restarts.

### 6.2 Receiver rules

A receiver tracks the highest `seq` it has accepted per direction. It MUST:

- **reject** `seq <= highest_accepted` with `replay_rejected` (§7.2), before decryption;
- **accept** `seq > highest_accepted`, including gaps — the relay's TTL purge creates
  legitimate gaps and a gap MUST NOT stall the stream;
- treat a large gap as a signal to request a fresh `snapshot`, not as an error.

Rejection happens on the header, before any decryption attempt, so a replayed envelope
costs a comparison rather than a crypto operation.

### 6.3 Clocks are not security inputs

`ts` is advisory: it drives "last seen 2m ago" in the UI and nothing else. A phone with a
wrong clock, or a relay that delays an envelope, MUST NOT be able to cause a security
decision to go the wrong way. Freshness comes from sequence numbers and the pairing
lifetime, never from comparing timestamps.

---

## 7. Versioning and errors

### 7.1 Version negotiation

There is none in v1, deliberately. `v` MUST be `1`; anything else is rejected with
`version_unsupported` **without attempting decryption**. A future v2 negotiates at pairing
time, not per envelope, so that a downgrade cannot be forced mid-session.

### 7.2 Error kinds

An `error` payload is `{code, detail?, ref_seq?}`. `detail` is for humans and MUST NOT
contain plaintext content.

| Code | Meaning |
| --- | --- |
| `version_unsupported` | `v` was not 1. |
| `replay_rejected` | `seq` was not greater than the highest accepted. |
| `decrypt_failed` | AEAD tag check failed — wrong key, tampering, or corruption. |
| `unknown_kind` | `kind` not recognised, or reserved-but-unimplemented. |
| `key_unknown` | `key_id` is not the active pairing's key. Checked **before** decryption (§5.3). |
| `bad_signature` | `device_sig` missing or invalid on a state-changing kind. |
| `rev_conflict` | `base_rev` did not match; see the `conflict` payload. |
| `pairing_unknown` | The relay has no Durable Object for this pairing. |
| `too_large` | Envelope exceeded the §3.1 limit. |

A receiver MUST NOT distinguish, in anything the relay can observe, between
`decrypt_failed` and `bad_signature` by timing or response size. Both are "this envelope
is not acceptable."

---

## 8. What this protocol cannot do

Stated as protocol properties, not implementation notes, so a future change that breaks
one is visible as a spec violation.

1. **No send path exists anywhere in this protocol.** There is no payload kind that causes
   the engine to transmit email. `doc_edit` updates a Gmail *draft* through the engine's
   existing compose-only Dispatcher. `CLAUDE.md` pins this: `Dispatcher.SubmitAsync`
   throws, and adding a sending kind here would be an invariant violation, not a feature.
2. **The phone holds no Gmail credentials** and this protocol never carries any. There is
   no kind that transports an OAuth token, refresh token, or provider API key.
3. **The relay cannot forge a command.** It has no key material. A modified header breaks
   the AAD; a modified body breaks the tag; a replayed envelope fails the seq check.
4. **The relay cannot read anything.** Every payload is inside the AEAD.
5. **An edit is the user's own words**, the same trust class as editing a draft in Gmail
   directly, so it does not re-run the Fabrication Gate. But an edited resume or cover
   letter loses its `verified` badge until a desktop re-verify pass runs, and `doc.verified`
   carries that state honestly rather than implying a verification that did not happen.
6. **Untrusted text stays data.** Job descriptions and recruiter text carried in `snapshot`
   or `delta` are display-only strings. They are never interpolated into anything
   executable, never rendered with active content, and never sent to a model from the
   phone.

---

## 9. Amendments to existing documents

This document changes text that is already written down. Recorded here so the change is
auditable rather than silent:

| Document | Was | Now |
| --- | --- | --- |
| `docs/CareerSeeker-Spec.md` §7.2 | XChaCha20-Poly1305 | AES-256-GCM (§5.1) |
| `docs/CareerSeeker-Spec.md` §7.2 | `{v, device, seq, ts, key_id, nonce, ciphertext}` | adds `pairing`; `device` becomes `dir` (§3) |
| `docs/CareerSeeker-Spec.md` §7.2 | event kinds listed as the shipping set | those kinds are **reserved for L2**; v1 ships the §4.3 set |
| `docs/CareerSeeker-Spec.md` §8.3 / this doc (P0) | X25519 pairing exchange | ECDH P-256 under a versioned `suite`; hybrid `p256+mlkem768` reserved (§5.2, P1) |
| This doc (P0) | Ed25519 `device_sig` inside the payload body | ECDSA P-256 as top-level `sig` over AAD+nonce+ciphertext-hash (§5.4, P1) |
| This doc (P0) | `doc_edit` body carries `device_sig` | field removed; the envelope `sig` covers it (§3, §5.4) |

`CareerSeeker-Spec.md` §7.2 is amended in the same commit that introduces this file. Two
documents disagreeing about a wire format is precisely the drift `CLAUDE.md` exists to
prevent.

---

## 10. Test vectors

`docs/sync-vectors/v1/` holds the shared vectors. **Both** the C# `SyncHarness` and the
Kotlin `:core` tests read these same files, so a divergence between the two
implementations fails CI instead of surfacing as a pairing bug in the field.

Vectors are generated by `docs/sync-vectors/generate.mjs`, which is committed and
deterministic — fixed test keys and fixed nonces, so re-running it produces byte-identical
output and a diff means a real change.

The generator is **Node**, while the first consumer is **C#**. That is deliberate: a
generator written in the same language as its verifier proves only that the language
agrees with itself.

> The keys and nonces in these vectors are published test values. They MUST NOT appear in
> any build, and fixed nonces are correct *only* here — reusing a nonce with a real key
> destroys AES-GCM's security entirely.

Each vector file:

```json
{
  "name": "delta-basic",
  "valid": true,
  "key_hex": "…64 hex chars…",
  "aad": "v=1|pairing=…|dir=e2p|seq=1|ts=…|key_id=…",
  "nonce_b64u": "…",
  "plaintext_json": { "kind": "delta", "body": { … } },
  "ciphertext_b64u": "…",
  "envelope_json": { … },
  "expect_error": null
}
```

Invalid vectors set `"valid": false` and name the required rejection in `expect_error`.
The suite MUST include at least: sequence regression (`replay_rejected`), truncated tag
(`decrypt_failed`), flipped AAD field (`decrypt_failed`), unknown key id, unknown payload
kind (`unknown_kind`), reserved-kind-in-v1 (`unknown_kind`), version mismatch
(`version_unsupported`), padded base64 (rejected), and an oversized envelope (`too_large`).

A conforming implementation decrypts every `valid` vector to the stated plaintext, and
rejects every invalid one **with the stated code**. Rejecting for the wrong reason is a
failure: it usually means a check fired earlier than intended and the real check is
untested.
