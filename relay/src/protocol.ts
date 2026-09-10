/**
 * Constants and header-only parsing for Sync Protocol v1.
 *
 * Normative source: docs/Sync-Protocol.md. Anything here that disagrees with that
 * document is a bug in this file.
 *
 * The relay parses the envelope HEADER and nothing else. It has no key material and
 * cannot decrypt `ciphertext`; it only needs enough structure to route, order, and
 * expire blobs. Adding a field here that requires understanding payload content would
 * break the property the whole design exists to preserve.
 */

export const PROTOCOL_VERSION = 1;

/**
 * The protocol's size cap, in the units the protocol states it: **decoded ciphertext
 * bytes** (docs/Sync-Protocol.md §3.1). Both receivers measure exactly this after
 * base64url-decoding, and neither of them ever sees this file.
 */
export const MAX_ENVELOPE_BYTES = 1024 * 1024;

/**
 * The same cap, converted into the units the relay can actually observe.
 *
 * The relay is blind: it never decodes `ciphertext`, so it cannot measure the quantity
 * §3.1 caps. All it can count is base64url characters. Unpadded base64url expands by
 * 4/3, so the largest *legal* ciphertext arrives as `ceil(4/3 × MAX_ENVELOPE_BYTES)`
 * characters — 1,398,102 for a 1 MiB cap.
 *
 * This constant must be DERIVED, never written as its own round number. Comparing a
 * character count against a byte budget is what this file did until 2026-08-09: the
 * guard read `ciphertext.length > MAX_ENVELOPE_BYTES`, which capped the *decoded*
 * payload at 786,432 bytes and made the top 256 KiB of the protocol's declared range
 * untransmittable. The relay refusing to carry what the spec declares legal is a worse
 * failure than the relay carrying 33% more bytes, because only the first one is silent —
 * it surfaces as a 413 on a §4.4 chunk sized correctly against §3.1.
 */
export const MAX_CIPHERTEXT_B64U_CHARS = Math.ceil((MAX_ENVELOPE_BYTES * 4) / 3);

/**
 * Headroom for the JSON scaffolding around the ciphertext — `v`, `pairing`, `dir`, `seq`,
 * `ts`, `key_id`, `nonce`, an optional `sig`, and the punctuation. That is ~200 bytes in
 * practice; 4 KiB is deliberate slack so the body guard never becomes the binding limit
 * and turns a size rule into a header-length rule.
 *
 * Note this counts UTF-16 code units (`String.length`), not bytes. For a well-formed
 * envelope every field is ASCII and the two agree; for a hostile body they do not, so
 * read this as a coarse resource guard on an already-materialised string and not as a
 * byte-exact limit. The binding protocol check is the ciphertext one above.
 */
export const MAX_PUSH_BODY_CHARS = MAX_CIPHERTEXT_B64U_CHARS + 4096;

/**
 * Retention ceiling. CareerSeeker-Spec.md section 8.3 caps relay retention at 30 days;
 * this constant is the ceiling, and a deployment may configure something shorter.
 * Nothing may raise it -- see the assertion in test/relay.test.ts.
 */
export const MAX_TTL_SECONDS = 30 * 24 * 60 * 60;

/** Default retention. Shorter than the ceiling on purpose: keep less, for less time. */
export const DEFAULT_TTL_SECONDS = 7 * 24 * 60 * 60;

/** Envelopes per pull page. Clients loop until `latest` is reached. */
export const PULL_PAGE_SIZE = 100;

export type Direction = 'e2p' | 'p2e';

export const DIRECTIONS: readonly Direction[] = ['e2p', 'p2e'];

/**
 * The header fields the relay reads. `ciphertext` is stored verbatim and never
 * inspected; there is deliberately no type here describing what is inside it,
 * because this codebase must not be able to express that.
 */
export interface EnvelopeHeader {
  v: number;
  pairing: string;
  dir: Direction;
  seq: number;
  ts: string;
  key_id: string;
}

/** Pairing ids are `p_` + 16 base64url chars. Opaque, and not derived from anything personal. */
const PAIRING_ID = /^p_[A-Za-z0-9_-]{16}$/;

export function isValidPairingId(value: string): boolean {
  return PAIRING_ID.test(value);
}

/**
 * The DO's storage schema. Every column is metadata or opaque bytes.
 *
 * There is no column for a user, an email address, a device name, or an IP. The
 * relay cannot answer "whose is this?" because it never had the information --
 * that is a design property, not an oversight, and the schema is where it is
 * enforced.
 */
export const ENVELOPE_TABLE_DDL = `
CREATE TABLE IF NOT EXISTS envelopes (
  dir        TEXT    NOT NULL,   -- 'e2p' | 'p2e'
  seq        INTEGER NOT NULL,   -- per-direction monotonic counter
  ts         TEXT    NOT NULL,   -- sender's clock, advisory only
  key_id     TEXT    NOT NULL,   -- which pairing key sealed it
  nonce      TEXT    NOT NULL,   -- base64url, 12 bytes
  ciphertext BLOB    NOT NULL,   -- opaque; the relay cannot read this
  size       INTEGER NOT NULL,
  expires_at INTEGER NOT NULL,   -- unix seconds; purged past this
  PRIMARY KEY (dir, seq)
);
CREATE INDEX IF NOT EXISTS envelopes_expiry ON envelopes (expires_at);
`;
