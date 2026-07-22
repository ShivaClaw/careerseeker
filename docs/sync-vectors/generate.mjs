#!/usr/bin/env node
// Generates the shared Sync Protocol v1 test vectors consumed by BOTH the C#
// SyncHarness (this repo) and the Kotlin :core tests (ShivaClaw/careerseeker-android).
//
// Deterministic by design: fixed test keys and fixed nonces, so re-running this
// produces byte-identical output and any diff is a real protocol change.
//
// Node is used deliberately even though the first consumer is C#. A generator
// written in the same language as its verifier proves only that the language
// agrees with itself.
//
//   node docs/sync-vectors/generate.mjs           # write vectors
//   node docs/sync-vectors/generate.mjs --check   # fail if on-disk vectors differ
//
// ============================ SECURITY NOTE ============================
// Every key and nonce below is a PUBLISHED TEST VALUE. They must never appear
// in a build. Fixed nonces are correct ONLY here -- reusing a nonce with a real
// key destroys AES-GCM's security completely.
// =======================================================================

import { createCipheriv, createHash } from 'node:crypto';
import { mkdirSync, readFileSync, readdirSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const OUT_DIR = join(dirname(fileURLToPath(import.meta.url)), 'v1');
const PROTOCOL_VERSION = 1;
const MAX_ENVELOPE_BYTES = 1024 * 1024;

/** base64url, unpadded (RFC 4648 section 5). The protocol rejects padded input. */
const b64u = (buf) => buf.toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

/** Test key material. Not secret, not usable, never in a build. */
const KEY_E2P = Buffer.from('a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90', 'hex');
const KEY_P2E = Buffer.from('0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0', 'hex');
const PAIRING = 'p_7Fq2mXk9LtVbN3wR';
const KEY_ID = 'k-2026-06-01';
const TS = '2026-06-11T14:02:11Z';

/**
 * The AAD is a deterministic ASCII string, not canonical JSON. Two independent
 * implementations must agree byte-for-byte, and JSON canonicalization (key
 * order, number formatting, Unicode escaping) is a classic cross-language
 * mismatch. Field order here is normative -- see Sync-Protocol.md section 4.1.
 */
function buildAad({ v, pairing, dir, seq, ts, key_id }) {
  return `v=${v}|pairing=${pairing}|dir=${dir}|seq=${seq}|ts=${ts}|key_id=${key_id}`;
}

/** AES-256-GCM. Returns ciphertext with the 16-byte tag appended. */
function seal(key, nonce, aad, plaintextObj) {
  const cipher = createCipheriv('aes-256-gcm', key, nonce);
  cipher.setAAD(Buffer.from(aad, 'ascii'));
  const body = Buffer.concat([
    cipher.update(Buffer.from(JSON.stringify(plaintextObj), 'utf8')),
    cipher.final(),
  ]);
  return Buffer.concat([body, cipher.getAuthTag()]);
}

/** Fixed per-vector nonce, derived from the vector name so it is stable and unique. */
function testNonce(name) {
  return createHash('sha256').update(`nonce/${name}`).digest().subarray(0, 12);
}

function makeVector({ name, valid, dir = 'e2p', seq, plaintext, expectError = null, notes,
                      v = PROTOCOL_VERSION, keyId = KEY_ID, mutate }) {
  const key = dir === 'e2p' ? KEY_E2P : KEY_P2E;
  const nonce = testNonce(name);
  const header = { v, pairing: PAIRING, dir, seq, ts: TS, key_id: keyId };
  const aad = buildAad(header);
  let ciphertext = seal(key, nonce, aad, plaintext);

  const vector = {
    name,
    valid,
    notes,
    key_hex: key.toString('hex'),
    aad,
    nonce_b64u: b64u(nonce),
    plaintext_json: plaintext,
    ciphertext_b64u: b64u(ciphertext),
    envelope_json: { ...header, nonce: b64u(nonce), ciphertext: b64u(ciphertext) },
    expect_error: expectError,
  };

  // Invalid vectors are built by corrupting a genuinely-valid envelope, so each
  // one tests exactly the check it names and nothing else.
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
    name: 'doc-edit-signed', valid: true, dir: 'p2e', seq: 1,
    plaintext: {
      kind: 'doc_edit',
      body: {
        app_id: 'app_01H8XK', doc_kind: 'cover_letter', base_rev: 4,
        new_text: 'Revised opening paragraph.',
        device_sig: 'ZmFrZS1zaWduYXR1cmUtZm9yLXZlY3RvcnMtb25seQ',
      },
    },
    notes: 'Phone-to-engine edit using the p2e key. device_sig here is a placeholder: Ed25519 '
         + 'verification has its own vectors in P1, once the pairing flow generates real keys.',
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
      const raw = Buffer.from(vec.ciphertext_b64u.replace(/-/g, '+').replace(/_/g, '/'), 'base64');
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
    notes: 'A reserved L2 kind must be rejected by a v1 receiver. This is the guard that stops the '
         + 'phone acquiring engine control before that has been audited.',
  }),
  makeVector({
    name: 'invalid-version', valid: false, dir: 'e2p', seq: 9, v: 2,
    plaintext: deltaBody, expectError: 'version_unsupported',
    notes: 'v=2. Must be rejected WITHOUT attempting decryption.',
  }),
];

// Two cases are structural rather than cryptographic, so they are written directly.
vectors.push({
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

// ---- write / check ----

const checkOnly = process.argv.includes('--check');
mkdirSync(OUT_DIR, { recursive: true });

const index = {
  version: PROTOCOL_VERSION,
  spec: 'docs/Sync-Protocol.md',
  generator: 'docs/sync-vectors/generate.mjs',
  cipher: 'AES-256-GCM',
  max_envelope_bytes: MAX_ENVELOPE_BYTES,
  // The pairing a conforming receiver should treat as active while running this suite.
  // Any other key_id must be rejected with key_unknown before decryption (spec 5.3).
  active_key_id: KEY_ID,
  warning: 'Published test keys and fixed nonces. Never use in a build.',
  vectors: vectors.map((v) => ({ name: v.name, valid: v.valid, expect_error: v.expect_error })),
};

const files = [
  ['index.json', index],
  ...vectors.map((v) => [`${v.name}.json`, v]),
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
  if (drift > 0) {
    console.error(`\n${drift} drift(s). Run: node docs/sync-vectors/generate.mjs`);
    process.exit(1);
  }
  console.log(`OK: ${files.length} vector files match the generator.`);
} else {
  const valid = vectors.filter((v) => v.valid).length;
  console.log(`Wrote ${files.length} files to docs/sync-vectors/v1/ (${valid} valid, ${vectors.length - valid} invalid).`);
}
