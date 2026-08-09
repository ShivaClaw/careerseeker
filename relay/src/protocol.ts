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

/** Envelope hard limit. Larger is rejected with HTTP 413 before any storage work. */
export const MAX_ENVELOPE_BYTES = 1024 * 1024;

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
