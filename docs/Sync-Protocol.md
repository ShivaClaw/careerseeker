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
| `/v1/{pairing}/pull?since={seq}&dir={dir}` | GET | Fetch envelopes for direction `dir` with `seq > since`. Response body in §2.1 |
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

### 2.1 Pull response body

**Decided 2026-08-11 (PQ-S4-2).** The route table above defines the pull *request* and
stopped there, so each of the three implementations invented its own reading of the
*response* — and `latest`, which §6.1 already depends on, was used by the normative text
without ever being defined. This section defines it.

```
200 body = {
  "envelopes": [ <envelope>, … ],   // REQUIRED — §3 envelopes, bare, ascending seq
  "latest": <integer>               // REQUIRED — highest seq the relay holds for dir
}
```

- Both fields are **REQUIRED**. A receiver MUST reject a 200 body that omits either one,
  and MUST NOT substitute a default for a missing field.
- `envelopes` is a JSON array of **bare §3 envelope objects**, spliced verbatim, in
  ascending `seq`, every one of them matching the requested `dir` and having
  `seq > since`. An empty array is legal and means "nothing above your cursor *in this
  page*" — see the truncation rule below. A receiver MUST NOT accept any other element
  shape; in particular it MUST NOT accept a `{"seq": N, "envelope": …}` wrapper.
- `latest` is a JSON **integer** — never a quoted number — and is the highest `seq` the
  relay currently holds for that direction, or `0` if it holds none.
- **The page MAY be truncated.** The relay bounds it (`PULL_PAGE_SIZE`, currently 100 —
  `relay/src/protocol.ts:64`), so a full page does not mean the stream is drained. A
  client MUST decide whether more remains by comparing its cursor against `latest`, and
  MUST NOT infer "caught up" from a short or empty `envelopes` array.
- A body that cannot be read under these rules MUST NOT be treated as an empty page: a
  receiver MUST NOT let it reach the caller as a successful pull of zero envelopes. It
  SHOULD be reported as a transport failure. The distinction is deliberate — the MUST is
  the safety property, and the SHOULD is reporting style, which the two receivers can
  reasonably differ on: the phone returns `RelayResult.Unavailable` because the failure
  lands in a background sync coroutine, while the engine's `PullAsync`
  (`src/Sync/RelayClient.cs`) lets the parse throw to its caller. Both refuse the silent
  empty page, which is the half that matters; neither is required to adopt the other's
  error type.

**Why both fields are required rather than defaulted, which is the load-bearing part.**
`latest` is the client's loop bound: it drives "is the relay still ahead of me" and §6.2's
gap check. A receiver that defaults an absent `latest` to `0` computes `cursor < 0`, which
is false, and therefore reports a healthy, fully-caught-up, permanently empty sync. **The
relay is the party that controls this body** (§1: blind, but not trusted), so that is one
deleted field standing between a working sync and a silent stall with no error anywhere.
Rejecting the body is loud; defaulting it is not. The same argument applies to `envelopes`
in mirror image — absent, defaulted to `[]`, it asserts "nothing to do" and "the relay is
ahead of you" in the same breath.

**Why the wrapper shape is refused.** No implementation emits it. The relay splices bare
envelope JSON (`relay/src/channel.ts`, `pull`: `rows.map((r) => r.ciphertext).join(',')`),
the engine's reader has no branch for it (`src/Sync/RelayClient.cs`, `PullAsync`), no test
vector contains a page at all, and this document never described one. It was accepted by
exactly one client and produced by none. Beyond being dead, it is **harmful**: the `seq` it
carries is the relay's own unauthenticated number, and it can disagree with the
authenticated `seq` inside the envelope that the AAD covers (§4.1). A client that advanced
a cursor on the wrapper's number could be walked past envelopes it never read by a relay
that cannot decrypt a byte of them. Any per-envelope sequence a client reads before
decryption is a **claim**; only the `seq` recovered from the sealed bytes is a fact, and
§6.2's rules run on the latter.

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

Every **structural** rejection — an unknown top-level field, padded base64, a nonce that is
not 12 bytes, a `dir` that is neither `e2p` nor `p2e`, a body that is not parseable JSON —
is reported as `decrypt_failed` (§7.2). v1 deliberately does **not** add a `malformed`
code: a distinct code would be a new observable, and §7.2 already requires that a receiver
not let an observer separate `decrypt_failed` from `bad_signature`. "This envelope is not
acceptable" is the whole of what a rejection may communicate. Amended in S5 (PQ-A2-2) to
state what both implementations already did rather than leave the code unnamed.

### 3.1 Size limits

The **decoded ciphertext** — the AEAD output including its 16-byte tag, after base64url
decoding — MUST NOT exceed **1 MiB**. A receiver measures those decoded bytes, not the
length of the JSON envelope and not the length of the base64url text, and rejects a larger
one with `too_large` (§7.2) *before* attempting any cryptography.

The relay cannot check that rule directly — it holds no key and never decodes `ciphertext`,
so the only quantity it can count is base64url characters. It therefore enforces the **same**
cap converted into its own units: a `ciphertext` string longer than
`ceil(4/3 × 1 MiB) = 1,398,102` characters, or a request body longer than that plus 4 KiB of
JSON scaffolding, is rejected with HTTP 413 (`relay/src/protocol.ts`
`MAX_CIPHERTEXT_B64U_CHARS` / `MAX_PUSH_BODY_CHARS`, applied in `relay/src/channel.ts`).

That conversion is normative, not incidental: **the relay MUST carry every envelope this
section declares legal.** A relay cap written as its own round number is a bug even when the
number looks conservative, because a sender obeying §3.1 and §4.4 has no way to discover it
except as a 413 on a correctly-sized chunk.

Document payloads that would exceed this are chunked at the payload layer (§4.4), not by
splitting ciphertext — a partial AEAD frame is not decryptable and must never be on the
wire.

Amended in S5 (PQ-A2-1). The P0 wording — "an envelope MUST NOT exceed 1 MiB total" —
described something neither implementation ever measured: both
(`src/Sync/EnvelopeReceiver.cs:45`, `core/.../EnvelopeReceiver.kt`) test the decoded
ciphertext. The prose was moved to the implementations rather than the reverse, because
the ciphertext is the thing that gets decrypted and because changing two shipping receivers
to satisfy a sentence would be a wire-visible change made for the sentence's sake. The
`invalid-oversized` vector pins the receiver rule: it sets a ciphertext of
`max_envelope_bytes + 1` **decoded** bytes.

Corrected later the same day, and the correction is the more interesting half. The first
version of this amendment observed that the relay's string test was *stricter* than the
receivers' byte test and concluded "an envelope the relay agrees to carry can never be one a
receiver rejects on size — so there is no gap". That implication is true and the conclusion
does not follow: it reasons in one direction only. Running the other direction against a local
relay showed the relay 413ing a ciphertext of exactly 1 MiB decoded — **legal by the sentence
at the top of this section, and refused by the transport**. The old guard compared a character
count to a byte budget, which capped the decoded payload at 786,432 bytes and left the top
**256 KiB** of the declared range untransmittable. Nothing sends envelopes that large today
(§4.4 chunking is unimplemented in both codebases), so this was latent rather than live — but
§4.4 instructs a future chunker to size against exactly the number that does not fit. The
relay now derives its cap from this section's, and `relay/test/relay.test.ts` pins the
derivation, the maximum legal envelope surviving a push/pull round trip, and the first
character beyond it.

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
| `snapshot` | Full dashboard state (counters + application/job summaries). Body in §4.3.1. Sent on pairing and on engine start. |
| `delta` | Recent-window dashboard state with a `since_seq` marker; receiver applies latest-wins. Body in §4.3.1. |
| `doc` | One document: `{app_id, doc_kind, rev, text, verified}`. `doc_kind` ∈ `draft_email` \| `cover_letter` \| `resume_text`. Not emitted in v1 — see the note after §4.3.1. |
| `evidence` | `{audit_ok, first_broken_seq?, event_count, events:[{seq, ts, actor, kind, entity, entity_id}]}` — the engine's audit-chain verdict plus recent event metadata. Never full event payload bodies. |
| `heartbeat` | `{ts, cycle, counters}`. Drives the app's "last seen" indicator. |
| `conflict` | Rejection of a `doc_edit`: `{app_id, doc_kind, base_rev, current_rev}`. |
| `entitlement_ack` | Engine confirms a verified Pro entitlement was applied. Body in §4.3.3. |
| `error` | §7.2. |

Phone → engine:

| Kind | Body |
| --- | --- |
| `doc_edit` | `{app_id, doc_kind, base_rev, new_text, device_sig}`. See §5.4 and §8. |
| `outcome` | Pro: `{app_id, outcome, at}`. `outcome` ∈ `sent` \| `replied` \| `interview` \| `offer` \| `rejected`. |
| `entitlement` | `{original_json, signature}` — Google Play's signed purchase payload. Body in §4.3.2. State-changing: the envelope MUST carry `sig` (§5.4). |
| `pull_request` | `{since_seq}` — ask the engine to re-publish the whole dashboard as a fresh `snapshot`. `since_seq` is **reserved in v1** and carries no meaning: senders MUST send `0`, receivers MUST ignore it. Body in §4.3.4. |
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

### 4.3.1 Dashboard bodies (`snapshot`, `delta`)

Both carry the same dashboard projection. `snapshot` is the full current state — the receiver
replaces its application and job tables wholesale — and `delta` is the recent window plus a
`since_seq` marker, which the receiver **upserts** (latest-wins by envelope seq). Scores are
**0–100 integers** on the wire: the engine scores on an internal 0–5 axis (`total = min(fit,
legitimacy) · multiplier`) and scales it by 20 before publishing, so the phone renders one scale
for demo and live data alike. No raw posting body ever rides here (§8.6) — only the short
structured fields below.

```
snapshot body = {
  "counters": { "discovered","acted","drafted","blocked","rejected","errors","cycles" },  // all long
  "applications": [ { "id","state","company","title","score","outcome"? } ],   // score: int 0–100; outcome: see below
  "jobs":         [ { "id","company","title","repost","injection_flag" } ]  // repost, injection_flag: bool
}

delta body = snapshot body + { "since_seq": <long> }
```

`outcome` is the **nullable** Pro outcome-tracking state (P4 §2.5), one of
`sent | no_reply | replied | interview | offer | rejected`, or **absent** when unset or non-Pro. It is
display-only, like every other carried string. It is the *store's* superset; the phone-set p2e `outcome`
kind (§4.3) carries the five-value subset without `no_reply`, which is a desktop-set observation. A
receiver treats an absent field as "no outcome", never as a malformed value.

`delta` currently carries the recent window (a bounded set), not a computed diff; `since_seq` is
the last envelope the publisher sent, and the receiver applies latest-wins over what it holds. A
large seq gap is a signal to request a fresh `snapshot` (§6.2), never to reconstruct missing
deltas. The field set is pinned here because it is the contract a third implementation reads —
the C# `SyncHarness` and the Kotlin applier tests each mirror it, but tests pin implementations,
not the wire.

**`doc` is specified but not emitted in v1 (opens P3).** The canonical `doc_kind` set is
`draft_email | cover_letter | resume_text` — the three documents spec §4.1 screen 4 renders;
screening-question answers are deliberately not a v1 `doc_kind`. Emitting `doc` requires engine
work that P3 opens with: the engine renders these to PDF and today persists only the file paths,
so it must first persist the tailored source **text** and a per-document **rev**, and decide how
the draft-email body — which lives in Gmail as a draft, not in the store — is sourced. Until that
lands, no implementation carries a `doc` branch, by the no-parser-for-unshipped-shapes rule.

### 4.3.2 Entitlement body (`entitlement`)

The phone is a **courier** for a Google-signed assertion; the engine is the verifier (gate
P0-WORKER option C — `docs/Entitlement-Architecture.md` in the android repo). The body is
exactly what Play Billing hands the phone:

```
entitlement body = {
  "original_json": <string>,   // Purchase.getOriginalJson() VERBATIM — the exact bytes the signature covers
  "signature":     <string>    // Purchase.getSignature() — see the encoding note below
}
```

`original_json` is Google's purchase record as a JSON **string** (fields include `orderId`,
`packageName`, `productId`, `purchaseTime`, `purchaseState`, `purchaseToken`, `acknowledged`).
The engine MUST verify over the **exact bytes** of this string and MUST NOT re-serialise it —
re-encoding would change the bytes the signature was made over. It is display/verify-only and,
like all carried text, stays inert (§8.6).

`signature` is **RSASSA-PKCS1-v1_5 over SHA-1** of the `original_json` UTF-8 bytes, made with
the app's Play-generated RSA key. The **public** half is the "License Key for This
Application" from Play Console, a base64 X.509 `SubjectPublicKeyInfo`. Two encoding facts are
load-bearing and differ from the rest of this protocol:

- `signature` is **standard base64** (alphabet `+` `/`, `=` padding), because that is what
  Play emits. It is payload *content*, not envelope framing, so the unpadded-base64url rule of
  §3/§4.1 does **not** apply to it — a receiver decodes it as standard base64.
- SHA-1 is Google's fixed IAB format, not a choice here. The assessment (not practically
  exploitable in this design; the Developer-API path stays a named seam) lives in
  `Entitlement-Architecture.md` §"weakness 1" and is not re-litigated in code.

The engine verifies, in order: the RSA signature over `original_json`; then `packageName ==`
the configured `applicationId` (`app.careerseeker.dashboard`); `productId ∈ {pro_unlock}`;
and `purchaseState == 0` (**PURCHASED in the raw JSON** — note `Purchase.getPurchaseState()`
remaps this to `1`, but the engine reads the raw JSON, whose purchased value is `0`). The Play
public key, the `applicationId`, and the product-id set are **configuration**, not constants
baked into the verifier — the production license key only exists once the Play app is created,
and slots in then. Each check has a distinct rejection reason (`generate.mjs` pins one negative
vector per reason: signature-invalid, wrong-product, wrong-package, not-purchased).

Because `entitlement` is state-changing (§5.4), the envelope also carries the device ECDSA
`sig`, so the audit chain records *which paired device* delivered the entitlement.

### 4.3.3 Entitlement acknowledgement body (`entitlement_ack`)

**Decided 2026-08-07 (gate PQ-A6-1, default-proceed).** `entitlement_ack` is the only thing
that may unlock Pro on the phone. §4.3.2 makes the phone a courier: it forwards the
Play-signed receipt, the engine verifies it against its configured public key, and this
kind is the engine's answer. Until S5 the kind had a name in the §4.3 table and no body at
all, which is why the phone-side unlock path could not be written — parsing an unspecified
shape would have been inventing wire format.

```
entitlement_ack body = {
  "product_id":      <string>,   // the product granted, e.g. "pro_unlock"
  "acknowledged_at": <string>,   // RFC 3339 UTC — when the ENGINE recorded the grant
  "order_id":        <string>    // OPTIONAL — Play orderId, for support correspondence
}
```

`product_id` MUST be one of the product ids the receiver already knows (§4.3.2's configured
set). A receiver that sees anything else MUST ignore the ack rather than unlock on it: the
field records *which* entitlement was granted, and is not a request to grant one.

`acknowledged_at` is the engine's clock and is **advisory only** (§6.3). It exists for
display and support. A receiver MUST NOT expire, re-lock, or refuse an entitlement on the
strength of this timestamp — clocks are not security inputs here, and an entitlement that
silently lapsed because two machines disagreed about the time would be indistinguishable
from a revocation nobody performed.

`order_id` is optional because it is Play correspondence data, not authorisation. An ack
without it is complete and MUST be honoured; an ack carrying it gains no additional weight.
It is present so a human can match a support ticket to a purchase.

**There is no negative form.** A receipt the engine rejects produces an `error` (§7.2)
naming the reason — never an `entitlement_ack` with a failure flag inside it. An ack means
*granted*, full stop. A kind whose meaning depends on reading a field inside the body is
exactly the parser hazard §4.2 exists to avoid, and here it would be a hazard on the one
path that turns a paid feature on.

Because `entitlement_ack` is `e2p`, the envelope MUST NOT carry `sig` (§3) — the engine
holds no device signing key. Its authenticity comes from the AEAD under the pairing's
`k_e2p`, which only the paired engine can produce; a relay that fabricated one would have
to forge a tag.

This body says nothing about *how* the phone stores the resulting state. That the unlock is
reachable only through an ack — and never through a locally-computed verdict on a receipt —
is a phone-side design boundary recorded as PQ-A2-4 in the android repo, and it is
load-bearing: a device that could self-certify its own entitlement would be a device with
an incentive to.

### 4.3.4 Pull request body (`pull_request`)

**Decided 2026-08-10 (PQ-S4-1, option (a)).** A `pull_request` means exactly one thing in
v1: *send me the current dashboard state as a fresh `snapshot`*. It is **not** resumable.

```
pull_request body = {
  "since_seq": <long>   // RESERVED in v1 — MUST be 0, MUST be ignored by the receiver
}
```

- A sender MUST set `since_seq` to `0`.
- A receiver MUST answer with a full `snapshot` — never a `delta`, and never a replay of
  the envelopes above some sequence point.
- A receiver MUST NOT reject a `pull_request` whose `since_seq` is non-zero. The field is
  reserved, not validated: a sender that fills it in honestly is asking for something v1
  cannot express, and a full snapshot is the correct answer to that question anyway.
  Rejecting it would turn a forward-compatible request into a stalled stream.

This replaces the earlier one-line description, "ask the engine to re-publish **from a
sequence point**", which described an intent that no implementation has ever had. What
ships on both sides today, measured rather than recalled:

- **Engine.** `InboundDispatcher` (`src/Sync/InboundDispatcher.cs:105-111`) parses the
  field — `ReadSinceSeq`, defaulting to `0` on any parse failure — and hands it to
  `ISnapshotRepublisher.RepublishSnapshotAsync(since, ct)`. **Every implementation of that
  interface ignores the argument.** `LiveRepublisher`
  (`tests/SyncLiveSmoke/Program.cs:311-312`) calls `PublishSnapshotAsync(...)`
  unconditionally; `RecordingRepublisher` (`tests/SyncHarness/Program.cs:756-758`) only
  records the value so the harness can assert it round-tripped. No shipping code path lets
  `since_seq` change what is sent.
- **Phone.** `PullPolicy` always sends `0`, for every reason it asks — cold start, a
  `delta` refused for want of a snapshot, or a §6.2 gap.

**Why the spec moves to the code rather than the code to the spec.** Resumption is not
merely unimplemented here; it conflicts with §6.2. A large gap is defined as a signal to
request *a fresh `snapshot`*, and a resumable pull cannot express "start over" — the two
features want the same kind to mean opposite things. Reporting a real high-water mark
would also encode a request the current engine ignores but a future one might honour, and
on the exact path where honouring it is wrong: the gap case would come back as deltas
resuming after N, which is what §6.2 says not to do. `0` is the only value that means "I
hold nothing usable, send everything" under both the engine that exists and the engine
that might.

**A v2 that wants resumption needs a different shape, not this field.** It needs a way to
ask for a snapshot *specifically* — a distinct kind, or an explicit discriminator — because
once a pull can resume, §6.2's "ask for a fresh snapshot" has no wire form left. Widening
`since_seq` in place would silently change what every v1 sender's `0` means.

**Note the field-name collision, because it is confusing on first read.** §4.3.1's `delta`
body also has a `since_seq`, and *that* one is live: it is the last envelope the publisher
sent, and the receiver applies latest-wins over what it holds. Same name, two fields, two
directions, and only one of them carries meaning. A reader who has seen `delta.since_seq`
work should not infer this one does.

This is a reserved **field**, which is a different thing from the reserved **kinds** listed
under §4.3: a reserved kind MUST be rejected as `unknown_kind`, while this field MUST be
accepted and ignored. The asymmetry is deliberate — rejecting an unknown kind refuses
traffic v1 cannot understand, whereas rejecting this field would refuse a request v1
understands perfectly.

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
**persisted by the sender across restarts**. This is load-bearing on the engine side: the phone
persists its highest-accepted e2p seq — it survives a process restart (a fresh in-memory receiver
would otherwise re-accept an old seq) — so an engine that resumed its counter at 1 after its own
restart would have every envelope, *including the recovery `snapshot`*, rejected as
`replay_rejected`: a silent, total, one-sided sync death. The engine MUST therefore resume its
e2p counter above `max(persisted_seq, relay_latest_e2p_seq)` — the value from its pairing store,
reconciled on startup against the relay's current `latest` for the direction
(`GET /pull?dir=e2p&since=0` returns it — the field is defined in §2.1) as a
belt-and-suspenders should the store lag. The
pairing store is the device-session deliverable; this rule is what it must satisfy.

### 6.2 Receiver rules

A receiver tracks the highest `seq` it has accepted per direction. It MUST:

- **reject** `seq <= highest_accepted` with `replay_rejected` (§7.2), before decryption;
- **accept** `seq > highest_accepted`, including gaps — the relay's TTL purge creates
  legitimate gaps and a gap MUST NOT stall the stream;
- treat a large gap as a signal to request a fresh `snapshot` (via `pull_request`, §4.3.4),
  not as an error.

**What counts as "large" is receiver policy, and v1 deliberately pins no number.** The
threshold cannot be a wire-level constant because only one side ever has an opinion: the
engine answers a `pull_request` but never sends one, so the number lives entirely in the
asking implementation and no third implementation can observe another's choice. A receiver
SHOULD document the value it picked and whether it was measured or chosen. The phone's is
a constructor parameter defaulting to **32**, labelled in its own source as chosen rather
than measured — there is no deployment to derive it from yet.

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
| `decrypt_failed` | AEAD tag check failed — wrong key, tampering, or corruption. **Also every structural rejection**: unknown top-level field, padded base64, wrong nonce length, unparseable framing (§3). There is deliberately no separate `malformed` code. |
| `unknown_kind` | `kind` not recognised, or reserved-but-unimplemented. |
| `key_unknown` | `key_id` is not the active pairing's key. Checked **before** decryption (§5.3). |
| `bad_signature` | `device_sig` missing or invalid on a state-changing kind. |
| `rev_conflict` | `base_rev` did not match; see the `conflict` payload. |
| `pairing_unknown` | The relay has no Durable Object for this pairing. |
| `too_large` | Envelope exceeded the §3.1 limit. |
| `unimplemented` | A recognised shipping kind the engine does not yet handle (e.g. inbound `doc_edit`, whose editing surface is P3). Distinct from `unknown_kind` — the kind IS known, so the phone should not treat it as a version/vocabulary error. |

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
| This doc (P0/§4.3) | `entitlement` body `{voucher}` (option-A entitlement Worker) | `{original_json, signature}` — the engine verifies Google Play's signature (gate P0-WORKER option C; §4.3.2, P4) |
| This doc (§4.3.1) | application summary `{id,state,company,title,score}` | adds nullable `outcome` — Pro outcome tracking, absent when unset/non-Pro (§4.3.1, P4 §2.5) |
| This doc (P0/§3.1) | "An envelope MUST NOT exceed **1 MiB** total" | the cap is on the **decoded ciphertext**, which is what both shipping receivers always measured; the relay's own limits are stated separately and are stricter (§3.1, S5 / PQ-A2-1) |
| This doc (§3) | structural rejection had no named error code | structural rejection reports `decrypt_failed`; no `malformed` code is added, so the observable set does not grow (§3, §7.2, S5 / PQ-A2-2) |
| This doc (§4.3) | `entitlement_ack` listed with no body | body is `{product_id, acknowledged_at, order_id?}` (§4.3.3, S5 / gate PQ-A6-1, default-proceed) |
| This doc (§4.3) | `pull_request` — "re-publish **from a sequence point**" | v1 `pull_request` is a whole-snapshot request; `since_seq` is **reserved**, MUST be `0`, MUST be ignored, and MUST NOT be a rejection reason (§4.3.4, S4 / PQ-S4-1 option (a)) |
| This doc (§6.2) | "treat a large gap as a signal to request a fresh `snapshot`" — no threshold | unchanged in substance; §6.2 now states explicitly that the threshold is **receiver policy** and that v1 pins no number, since only the asking side ever has an opinion (§6.2, S4 / PQ-S4-1) |

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

#### 10.1 The `type` field, and why a new kind gets its own

Every vector carries a `type`. Vectors of type `envelope` go through the generic
round-trip, AAD-tampering and receiver loops in **both** consumers; `pairing`,
`entitlement` and `entitlement_ack` vectors are read by dedicated sections instead. Both
consumers filter on the same string (`tests/SyncHarness/Program.cs:62`,
`core/.../ProtocolVectorsTest.kt:55`).

That partition is load-bearing, not cosmetic. The `envelope` suite is fed through a
**single receiver in sequence order** — valid vectors first, then invalid ones — so its
`seq` space is fully packed by design: the valid `e2p` vectors occupy 1–4 and every invalid
`e2p` vector sits above them, relying on the high-water mark staying at 4. Adding a new
*valid* `e2p` envelope vector would raise that mark past `invalid-truncated-tag` (seq 5)
and `invalid-unknown-kind` (seq 8), whose expected `decrypt_failed` and `unknown_kind`
would silently become `replay_rejected` — the replay check runs before both
(`src/Sync/EnvelopeReceiver.cs:53`). There is no integer that avoids this: the vector would
need `seq > 4` to be accepted and `seq < 5` to be harmless.

So a new payload kind is introduced under its **own `type`**, consumed by a dedicated
section, exactly as `entitlement` was. Renumbering existing vectors is not an option: their
bytes are a published wire artifact that a second repository vendors at a pinned commit.

#### 10.2 `entitlement-ack` is specified and pinned, not yet asserted

The `entitlement-ack` and `entitlement-ack-no-order-id` vectors (S5) pin §4.3.3's body.
**No consumer asserts against them yet.** The C# and Kotlin appliers arrive in the same
rung; until they do, these files are a fixed target for those appliers to be written
against, and are *not* evidence that either implementation handles `entitlement_ack`. The
pair exists so that `order_id`'s optionality is pinned by an artifact rather than by prose:
one vector carries it, one does not, and both are valid.
