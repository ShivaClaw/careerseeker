#!/usr/bin/env node
// Generates the shared Sync Protocol v1 test vectors consumed by BOTH the C#
// SyncHarness (this repo) and the Kotlin :core tests (ShivaClaw/careerseeker-android).
//
// Deterministic by design: fixed test keys and fixed nonces, so re-running this
// produces byte-identical output and any diff is a real protocol change.
//
// ECDSA is the one non-deterministic primitive (random k per signature). Signature
// vectors therefore use an EMBED-AND-VERIFY pattern: the signature bytes are constants
// in SIG_CONSTANTS below, and every run VERIFIES them against the fixed keys and inputs
// rather than re-signing. If a constant is missing or stale, the generator signs fresh,
// prints the value to paste, and exits nonzero -- so CI can never silently mint new
// signatures.
//
// Node is used deliberately even though the consumers are C# and Kotlin. A generator
// written in the same language as its verifier proves only that the language agrees
// with itself.
//
//   node docs/sync-vectors/generate.mjs           # write vectors
//   node docs/sync-vectors/generate.mjs --check   # fail if on-disk vectors differ
//
// ============================ SECURITY NOTE ============================
// Every key, secret, and nonce below is a PUBLISHED TEST VALUE. They must never
// appear in a build. Fixed nonces are correct ONLY here -- reusing a nonce with a
// real key destroys AES-GCM's security completely.
// =======================================================================

import {
  createCipheriv, createDecipheriv, createECDH, createHash, createPrivateKey,
  createPublicKey, hkdfSync, sign as cryptoSign, verify as cryptoVerify,
} from 'node:crypto';
import { mkdirSync, readFileSync, readdirSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const OUT_DIR = join(dirname(fileURLToPath(import.meta.url)), 'v1');
const PROTOCOL_VERSION = 1;
const SUITE = 'p256-hkdf-sha256';
const MAX_ENVELOPE_BYTES = 1024 * 1024;

/** base64url, unpadded (RFC 4648 section 5). The protocol rejects padded input. */
const b64u = (buf) => buf.toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
const unb64u = (s) => Buffer.from(s.replace(/-/g, '+').replace(/_/g, '/'), 'base64');
const sha256 = (buf) => createHash('sha256').update(buf).digest();

// ---------------------------------------------------------------- fixed test material

/** Symmetric test keys for the envelope vectors (as in P0). */
const KEY_E2P = Buffer.from('a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90', 'hex');
const KEY_P2E = Buffer.from('0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0', 'hex');
const PAIRING = 'p_7Fq2mXk9LtVbN3wR';
const KEY_ID = 'k-2026-06-01';
const TS = '2026-06-11T14:02:11Z';

/**
 * Fixed P-256 private scalars for the pairing vectors. Chosen arbitrarily (valid range),
 * published, never for real use. Public points are computed, not pasted, so they cannot
 * drift from the scalars.
 */
const ENGINE_D = Buffer.from('4f2a1c8be5d3907612384a5b6c7d8e9fa0b1c2d3e4f5061728394a5b6c7d8e9f', 'hex');
const PHONE_D  = Buffer.from('137f5b3d9c2e8a4061725384a5b6c7d8e9f0a1b2c3d4e5f60718293a4b5c6d7e', 'hex');
const DEVICE_D = Buffer.from('29c4e6a80b1d3f52647586a9bacbdcedfe0f102132435465768798a9bacbdcde', 'hex');
const REVOKED_D = Buffer.from('0badc0de0badc0de0badc0de0badc0de0badc0de0badc0de0badc0de0badc0de', 'hex');

/** One-time pairing secret from the QR. 32 bytes. */
const PAIR_SECRET = Buffer.from('5ec1e7a2b3c4d5e6f708192a3b4c5d6e7f808192a3b4c5d6e7f8091a2b3c4d5e', 'hex');

// ---------------------------------------------------------------- P-256 helpers

function ecdhKey(dBuf) {
  const ecdh = createECDH('prime256v1');
  ecdh.setPrivateKey(dBuf);
  return { ecdh, pub: ecdh.getPublicKey() }; // uncompressed 65-byte point
}

/** Build a node KeyObject pair from a raw P-256 scalar, for ECDSA sign/verify. */
function ecdsaKeys(dBuf) {
  const { pub } = ecdhKey(dBuf);
  // PKCS8 wrap of an EC private key: fixed template for prime256v1 with embedded d + pub.
  const pkcs8 = Buffer.concat([
    Buffer.from('308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b0201010420', 'hex'),
    dBuf,
    Buffer.from('a144034200', 'hex'),
    pub,
  ]);
  const spki = Buffer.concat([
    Buffer.from('3059301306072a8648ce3d020106082a8648ce3d030107034200', 'hex'),
    pub,
  ]);
  return {
    priv: createPrivateKey({ key: pkcs8, format: 'der', type: 'pkcs8' }),
    pubKey: createPublicKey({ key: spki, format: 'der', type: 'spki' }),
    pub,
  };
}

const P1363 = { dsaEncoding: 'ieee-p1363' }; // raw r||s, 64 bytes -- no DER

// ---------------------------------------------------------------- AAD + sealing

/**
 * The AAD is a deterministic ASCII string, not canonical JSON. Two independent
 * implementations must agree byte-for-byte, and JSON canonicalization (key order,
 * number formatting, Unicode escaping) is a classic cross-language mismatch.
 * Field order is normative -- see Sync-Protocol.md section 4.1.
 */
function buildAad({ v, pairing, dir, seq, ts, key_id }) {
  return `v=${v}|pairing=${pairing}|dir=${dir}|seq=${seq}|ts=${ts}|key_id=${key_id}`;
}

/** AES-256-GCM. Returns ciphertext with the 16-byte tag appended. */
function seal(key, nonce, aad, plaintextBytes) {
  const cipher = createCipheriv('aes-256-gcm', key, nonce);
  cipher.setAAD(Buffer.from(aad, 'ascii'));
  const body = Buffer.concat([cipher.update(plaintextBytes), cipher.final()]);
  return Buffer.concat([body, cipher.getAuthTag()]);
}

function open(key, nonce, aad, sealed) {
  const d = createDecipheriv('aes-256-gcm', key, nonce);
  d.setAAD(Buffer.from(aad, 'ascii'));
  d.setAuthTag(sealed.subarray(sealed.length - 16));
  return Buffer.concat([d.update(sealed.subarray(0, sealed.length - 16)), d.final()]);
}

/** Fixed per-vector nonce, derived from the vector name so it is stable and unique. */
function testNonce(name) {
  return sha256(Buffer.from(`nonce/${name}`)).subarray(0, 12);
}

/**
 * Signature input per Sync-Protocol.md section 5.4:
 *   "careerseeker/v1/cmd|" + AAD + "|" + nonce_b64u + "|" + sha256-hex(ciphertext bytes)
 * Everything in it is an exact wire artifact -- nothing to canonicalise.
 */
function sigInput(aad, nonceB64u, ciphertext) {
  return `careerseeker/v1/cmd|${aad}|${nonceB64u}|${sha256(ciphertext).toString('hex')}`;
}

// ---------------------------------------------------------------- embedded signatures
//
// Paste values printed by a run where a constant is missing. Every run re-VERIFIES
// these against the fixed keys and the deterministic sig inputs.

const SIG_CONSTANTS = {
  'doc-edit-signed': '885b4073064938a33c392ae9b76adfb2e0943eace32f46fbfca4e6edccc5dc32042b939a827147ebcb06811886cc55425d9813eeead0836241f636f8a485bec7',
  'sig-tampered': 'dc4fc1fba734e2d35e9d583c5aaf1520f04714b34169573f1f594e72005d9040357beffa23913aa8a2c36fabbddafbdafb8013054bbaabf3cf95944796b51978',
  'sig-by-revoked-key': '1dceb4541a31cfb5448cec45d7ca4296511fdf6db9c6918374a21d0710198b510f04e67c09327141d01ddb91925ef161920833a1639c8eb5c74166ea50128bae',
};

// ---------------------------------------------------------------- pairing derivations

const engine = ecdhKey(ENGINE_D);
const phone = ecdhKey(PHONE_D);
const device = ecdsaKeys(DEVICE_D);
const revoked = ecdsaKeys(REVOKED_D);

// ECDH-P256: node returns the shared X coordinate, 32 bytes -- the `ss` of section 5.2.
const SS = engine.ecdh.computeSecret(phone.pub);

/**
 * ikm = concat(ss): one element in v1. The concat is load-bearing -- the hybrid suite
 * appends the ML-KEM shared secret here, and nothing else about derivation changes.
 */
const IKM = Buffer.concat([SS]);

const hk = (info, len, ikm = IKM, salt = PAIR_SECRET) =>
  Buffer.from(hkdfSync('sha256', ikm, salt, info, len));

const K_E2P_DERIVED = hk('careerseeker/v1/e2p', 32);
const K_P2E_DERIVED = hk('careerseeker/v1/p2e', 32);
const RELAY_TOKEN = b64u(hk('careerseeker/v1/relay-token', 32));
const CONFIRM = String(hk('careerseeker/v1/confirm', 4).readUInt32BE(0) % 1_000_000).padStart(6, '0');
// Provisional token (section 5.2.1): keyed on the one-time secret alone.
const PROVISIONAL_TOKEN = b64u(Buffer.from(
  hkdfSync('sha256', PAIR_SECRET, Buffer.from('careerseeker/v1/bootstrap', 'ascii'), 'careerseeker/v1/relay-token', 32),
));

// Pairing completion (section 5.2.2), sealed with the DERIVED p2e key.
const PAIR_AAD = `careerseeker/v1/pair|${PAIRING}|${SUITE}|${b64u(phone.pub)}`;
const PAIR_NONCE = testNonce('pairing-completion');
const PAIR_PAYLOAD = Buffer.from(JSON.stringify({ device_sig_pub: b64u(device.pub), ts: TS }), 'utf8');
const PAIR_CIPHERTEXT = seal(K_P2E_DERIVED, PAIR_NONCE, PAIR_AAD, PAIR_PAYLOAD);

// ---------------------------------------------------------------- envelope vectors

function makeVector({ name, valid, dir = 'e2p', seq, plaintext, expectError = null, notes,
                      v = PROTOCOL_VERSION, keyId = KEY_ID, sigWith = null, sigTamper = false,
                      omitSig = false, mutate }) {
  const key = dir === 'e2p' ? KEY_E2P : KEY_P2E;
  const nonce = testNonce(name);
  const header = { v, pairing: PAIRING, dir, seq, ts: TS, key_id: keyId };
  const aad = buildAad(header);
  const ciphertext = seal(key, nonce, aad, Buffer.from(JSON.stringify(plaintext), 'utf8'));

  const envelope = { ...header, nonce: b64u(nonce), ciphertext: b64u(ciphertext) };

  let sigHex = null;
  if (sigWith && !omitSig) {
    const input = sigInput(aad, b64u(nonce), ciphertext);
    const constant = SIG_CONSTANTS[name];
    if (!constant) {
      const fresh = cryptoSign(null, Buffer.from(input, 'ascii'), { key: sigWith.priv, ...P1363 });
      console.error(`MISSING SIG_CONSTANT for '${name}'. Paste into SIG_CONSTANTS:\n  '${name}': '${fresh.toString('hex')}',`);
      process.exitCode = 1;
      sigHex = fresh.toString('hex');
    } else {
      const ok = cryptoVerify(null, Buffer.from(input, 'ascii'), { key: sigWith.pubKey, ...P1363 }, Buffer.from(constant, 'hex'));
      if (!ok) {
        console.error(`STALE SIG_CONSTANT for '${name}': fails verification against current inputs. Delete it and re-run to mint.`);
        process.exitCode = 1;
      }
      sigHex = constant;
    }
    let sigBytes = Buffer.from(sigHex, 'hex');
    if (sigTamper) { sigBytes = Buffer.from(sigBytes); sigBytes[7] ^= 0xff; }
    envelope.sig = b64u(sigBytes);
  }

  const vector = {
    type: 'envelope',
    name,
    valid,
    notes,
    key_hex: key.toString('hex'),
    aad,
    nonce_b64u: b64u(nonce),
    plaintext_json: plaintext,
    ciphertext_b64u: b64u(ciphertext),
    sig_input: sigWith ? sigInput(aad, b64u(nonce), ciphertext) : undefined,
    device_sig_pub_b64u: sigWith ? b64u(sigWith.pub) : undefined,
    envelope_json: envelope,
    expect_error: expectError,
  };

  return mutate ? mutate(vector) : vector;
}

const deltaBody = {
  kind: 'delta',
  body: {
    since_seq: 48210,
    applications: [
      { id: 'app_01H8XK', state: 'READY', company: 'Northwind Labs', title: 'Senior Platform Engineer', score: 82 },
    ],
    counters: { discovered: 3, drafted: 1, blocked: 0 },
  },
};

const docEditBody = {
  kind: 'doc_edit',
  // No device_sig in the body (P1 amendment): the signature is the envelope-level `sig`,
  // over exact wire bytes, so neither implementation ever canonicalises JSON to verify.
  body: { app_id: 'app_01H8XK', doc_kind: 'cover_letter', base_rev: 4, new_text: 'Revised opening paragraph.' },
};

const vectors = [
  makeVector({
    name: 'delta-basic', valid: true, dir: 'e2p', seq: 1, plaintext: deltaBody,
    notes: 'Baseline engine-to-phone delta. Round-trips to plaintext_json exactly.',
  }),
  makeVector({
    name: 'doc-draft-email', valid: true, dir: 'e2p', seq: 2,
    plaintext: {
      kind: 'doc',
      body: {
        app_id: 'app_01H8XK', doc_kind: 'draft_email', rev: 4, verified: true,
        text: 'Hello,\n\nI am writing about the Senior Platform Engineer role.\n\nRegards,\nB.',
      },
    },
    notes: 'Document payload with multi-line text and a rev. Exercises UTF-8 and newlines.',
  }),
  makeVector({
    name: 'doc-edit-signed', valid: true, dir: 'p2e', seq: 1, plaintext: docEditBody,
    sigWith: device,
    notes: 'Phone-to-engine edit with the REAL envelope-level ECDSA P-256 signature '
         + '(P1: replaces the P0 placeholder that sat inside the body). Consumers must '
         + 'verify sig over sig_input using device_sig_pub, then decrypt.',
  }),
  makeVector({
    name: 'heartbeat-unicode', valid: true, dir: 'e2p', seq: 3,
    plaintext: { kind: 'heartbeat', body: { ts: TS, cycle: 12, counters: { discovered: 3 }, note: 'café — naïve 日本語 🙂' } },
    notes: 'Non-ASCII plaintext. Catches implementations that treat UTF-8 as Latin-1 or mangle surrogate pairs.',
  }),
  makeVector({
    name: 'empty-body', valid: true, dir: 'e2p', seq: 4,
    plaintext: { kind: 'snapshot', body: {} },
    notes: 'Empty object body. AES-GCM over a short plaintext still produces a full 16-byte tag.',
  }),

  // ---- invalid: each corrupts a valid envelope in exactly one way ----

  makeVector({
    name: 'invalid-seq-regression', valid: false, dir: 'e2p', seq: 1,
    plaintext: deltaBody, expectError: 'replay_rejected',
    notes: 'seq 1 arriving after seq 4 was accepted. Must be rejected on the header, BEFORE '
         + 'any decryption attempt. Consume this vector after delta-basic..empty-body.',
  }),
  makeVector({
    name: 'invalid-truncated-tag', valid: false, dir: 'e2p', seq: 5,
    plaintext: deltaBody, expectError: 'decrypt_failed',
    notes: 'Last 4 bytes of the 16-byte GCM tag removed. Must fail authentication, not throw a parse error.',
    mutate: (vec) => {
      const raw = unb64u(vec.ciphertext_b64u);
      const cut = raw.subarray(0, raw.length - 4);
      return { ...vec, ciphertext_b64u: b64u(cut), envelope_json: { ...vec.envelope_json, ciphertext: b64u(cut) } };
    },
  }),
  makeVector({
    name: 'invalid-aad-tampered-seq', valid: false, dir: 'e2p', seq: 6,
    plaintext: deltaBody, expectError: 'decrypt_failed',
    notes: 'Envelope seq rewritten to 9999 after sealing, as a malicious relay would. The AAD no '
         + 'longer matches, so the tag check fails. This is the vector that proves header fields '
         + 'are actually bound into the AEAD.',
    mutate: (vec) => ({ ...vec, envelope_json: { ...vec.envelope_json, seq: 9999 } }),
  }),
  makeVector({
    name: 'invalid-unknown-key-id', valid: false, dir: 'e2p', seq: 7,
    plaintext: deltaBody, keyId: 'k-2020-01-01', expectError: 'key_unknown',
    notes: 'Sealed under a SUPERSEDED key id but with key material that still decrypts. This is the '
         + 'revoked-device case: the tag check cannot see it, so key_id must be checked explicitly '
         + 'against the active pairing BEFORE decryption. A receiver that answers decrypt_failed here '
         + 'is relying on cryptography to enforce revocation, which it does not do.',
  }),
  makeVector({
    name: 'invalid-unknown-kind', valid: false, dir: 'e2p', seq: 8,
    plaintext: { kind: 'telemetry_upload', body: { anything: true } }, expectError: 'unknown_kind',
    notes: 'Decrypts cleanly, then fails on kind. Proves the kind check is real and not implied by decryption.',
  }),
  makeVector({
    name: 'invalid-reserved-kind-l2', valid: false, dir: 'p2e', seq: 2,
    plaintext: { kind: 'kill', body: { scope: 'all' } }, expectError: 'unknown_kind',
    notes: 'A reserved L2 kind must be rejected by a v1 receiver (checked BEFORE the sig requirement: '
         + 'unknown_kind wins over bad_signature). This is the guard that stops the phone acquiring '
         + 'engine control before that has been audited.',
  }),
  makeVector({
    name: 'invalid-version', valid: false, dir: 'e2p', seq: 9, v: 2,
    plaintext: deltaBody, expectError: 'version_unsupported',
    notes: 'v=2. Must be rejected WITHOUT attempting decryption.',
  }),

  // ---- signature negatives (P1) ----

  makeVector({
    name: 'sig-tampered', valid: false, dir: 'p2e', seq: 3, plaintext: docEditBody,
    sigWith: device, sigTamper: true, expectError: 'bad_signature',
    notes: 'Valid envelope, one flipped byte in sig. Decrypts fine; must still be rejected. '
         + 'Uses the doc-edit-signed constant tampered in-flight, so no second signing key is involved.',
    mutate: (vec) => vec, // sig constant reuse: same input? No -- seq differs, so input differs.
  }),
  makeVector({
    name: 'sig-by-revoked-key', valid: false, dir: 'p2e', seq: 4, plaintext: docEditBody,
    sigWith: revoked, expectError: 'bad_signature',
    notes: 'Correctly-formed ECDSA signature by a key that is NOT the pairing\'s device key. '
         + 'Verifies under the wrong pubkey, so consumers must check against device_sig_pub of the '
         + 'PAIRING, not whatever key the envelope claims. device_sig_pub_b64u here is the revoked '
         + 'key so the vector is self-contained: verify against pairing-basic\'s device key and reject.',
  }),
  makeVector({
    name: 'sig-missing-on-doc-edit', valid: false, dir: 'p2e', seq: 5, plaintext: docEditBody,
    expectError: 'bad_signature',
    notes: 'State-changing p2e kind with no sig field at all. Rejected after decryption reveals the kind.',
  }),
  makeVector({
    name: 'sig-on-e2p', valid: false, dir: 'e2p', seq: 10, plaintext: deltaBody,
    expectError: 'bad_signature',
    notes: 'An e2p envelope must never carry sig; the engine has no device key. Field injected post-seal.',
    mutate: (vec) => ({ ...vec, envelope_json: { ...vec.envelope_json, sig: 'AAAA' } }),
  }),
];

// ---------------------------------------------------------------- structural cases

vectors.push({
  type: 'envelope',
  name: 'invalid-padded-base64',
  valid: false,
  notes: 'Padded base64 must be rejected rather than leniently accepted. Accepting both spellings '
       + 'means the vectors no longer pin one encoding.',
  key_hex: KEY_E2P.toString('hex'),
  aad: buildAad({ v: 1, pairing: PAIRING, dir: 'e2p', seq: 10, ts: TS, key_id: KEY_ID }),
  nonce_b64u: 'AAAAAAAAAAAAAAAA==',
  plaintext_json: null,
  ciphertext_b64u: 'AAAA==',
  envelope_json: {
    v: 1, pairing: PAIRING, dir: 'e2p', seq: 10, ts: TS, key_id: KEY_ID,
    nonce: 'AAAAAAAAAAAAAAAA==', ciphertext: 'AAAA==',
  },
  expect_error: 'decrypt_failed',
});

vectors.push({
  type: 'envelope',
  name: 'invalid-oversized',
  valid: false,
  notes: `Envelope exceeds the ${MAX_ENVELOPE_BYTES}-byte limit. Rejected on size before any crypto. `
       + 'ciphertext_b64u is given as a repeat count rather than inline so this file stays readable; '
       + 'consumers synthesize it as "A" repeated synth_ciphertext_len times.',
  key_hex: KEY_E2P.toString('hex'),
  aad: buildAad({ v: 1, pairing: PAIRING, dir: 'e2p', seq: 11, ts: TS, key_id: KEY_ID }),
  nonce_b64u: b64u(testNonce('invalid-oversized')),
  plaintext_json: null,
  ciphertext_b64u: null,
  synth_ciphertext_len: MAX_ENVELOPE_BYTES + 1,
  envelope_json: null,
  expect_error: 'too_large',
});

// ---------------------------------------------------------------- pairing vectors (P1)

const pairingVectors = [
  {
    type: 'pairing',
    name: 'pairing-basic',
    valid: true,
    notes: 'Full section-5.2 derivation from fixed keys: ECDH P-256 -> ikm=concat(ss) -> HKDF '
         + 'directional keys, relay token, provisional token, 6-digit confirm code -- plus a '
         + 'sealed completion message. A conforming implementation reproduces every derived value '
         + 'byte-for-byte and decrypts the completion to the stated payload.',
    suite: SUITE,
    secret_b64u: b64u(PAIR_SECRET),
    engine: { d_hex: ENGINE_D.toString('hex'), pub_b64u: b64u(engine.pub) },
    phone: { d_hex: PHONE_D.toString('hex'), pub_b64u: b64u(phone.pub) },
    device_sig: { d_hex: DEVICE_D.toString('hex'), pub_b64u: b64u(device.pub) },
    expected: {
      ss_hex: SS.toString('hex'),
      k_e2p_hex: K_E2P_DERIVED.toString('hex'),
      k_p2e_hex: K_P2E_DERIVED.toString('hex'),
      relay_token_b64u: RELAY_TOKEN,
      provisional_token_b64u: PROVISIONAL_TOKEN,
      confirm: CONFIRM,
    },
    completion: {
      aad: PAIR_AAD,
      nonce_b64u: b64u(PAIR_NONCE),
      payload_json: JSON.parse(PAIR_PAYLOAD.toString('utf8')),
      ciphertext_b64u: b64u(PAIR_CIPHERTEXT),
    },
    expect_error: null,
  },
  (() => {
    // MITM key substitution: attacker swaps phone_pub for their own. The AAD binds
    // phone_pub, so decryption of the completion MUST fail even though the attacker's
    // key would derive a *different* k_p2e anyway -- this vector proves the binding.
    const attacker = ecdhKey(Buffer.from('66f2d4a8c1b3e5977a8b9cadbecfd0e1f2031425364758697a8b9cadbecfd0e1', 'hex'));
    const tamperAad = `careerseeker/v1/pair|${PAIRING}|${SUITE}|${b64u(attacker.pub)}`;
    return {
      type: 'pairing',
      name: 'pairing-mitm-keyswap',
      valid: false,
      notes: 'The completion ciphertext from pairing-basic presented with the ATTACKER\'s phone_pub '
           + '(and therefore attacker AAD). Decryption must fail: phone_pub is bound into the AAD, '
           + 'so a relay that swaps keys breaks the tag. This is the vector that proves the pairing '
           + 'handshake resists a malicious relay.',
      suite: SUITE,
      secret_b64u: b64u(PAIR_SECRET),
      engine: { d_hex: ENGINE_D.toString('hex'), pub_b64u: b64u(engine.pub) },
      phone: { d_hex: PHONE_D.toString('hex'), pub_b64u: b64u(attacker.pub) },
      device_sig: { d_hex: DEVICE_D.toString('hex'), pub_b64u: b64u(device.pub) },
      expected: null,
      completion: {
        aad: tamperAad,
        nonce_b64u: b64u(PAIR_NONCE),
        payload_json: null,
        ciphertext_b64u: b64u(PAIR_CIPHERTEXT),
      },
      expect_error: 'decrypt_failed',
    };
  })(),
];

// Sanity: verify the valid completion opens, so the suite can never ship self-inconsistent.
open(K_P2E_DERIVED, PAIR_NONCE, PAIR_AAD, PAIR_CIPHERTEXT);

const all = [...vectors, ...pairingVectors];

// ---------------------------------------------------------------- write / check

const checkOnly = process.argv.includes('--check');
mkdirSync(OUT_DIR, { recursive: true });

const index = {
  version: PROTOCOL_VERSION,
  suite: SUITE,
  spec: 'docs/Sync-Protocol.md',
  generator: 'docs/sync-vectors/generate.mjs',
  cipher: 'AES-256-GCM',
  max_envelope_bytes: MAX_ENVELOPE_BYTES,
  active_key_id: KEY_ID,
  pairing_id: PAIRING,
  warning: 'Published test keys and fixed nonces. Never use in a build.',
  vectors: all.map((v) => ({ name: v.name, type: v.type, valid: v.valid, expect_error: v.expect_error })),
};

const clean = (obj) => JSON.parse(JSON.stringify(obj)); // drop undefined fields
const files = [
  ['index.json', index],
  ...all.map((v) => [`${v.name}.json`, clean(v)]),
];

let drift = 0;
for (const [filename, content] of files) {
  const path = join(OUT_DIR, filename);
  const text = JSON.stringify(content, null, 2) + '\n';
  if (checkOnly) {
    const actual = existsSync(path) ? readFileSync(path, 'utf8') : null;
    if (actual !== text) {
      console.error(`DRIFT: ${filename} ${actual === null ? 'is missing' : 'differs from generator output'}`);
      drift++;
    }
  } else {
    writeFileSync(path, text);
  }
}

if (checkOnly) {
  const expected = new Set(files.map(([f]) => f));
  for (const found of readdirSync(OUT_DIR)) {
    if (!expected.has(found)) {
      console.error(`DRIFT: ${found} is on disk but not produced by the generator`);
      drift++;
    }
  }
  if (drift > 0 || process.exitCode) {
    console.error(`\n${drift} drift(s). Run: node docs/sync-vectors/generate.mjs`);
    process.exit(1);
  }
  console.log(`OK: ${files.length} vector files match the generator.`);
} else if (!process.exitCode) {
  const valid = all.filter((v) => v.valid).length;
  console.log(`Wrote ${files.length} files to docs/sync-vectors/v1/ (${valid} valid, ${all.length - valid} invalid).`);
}
