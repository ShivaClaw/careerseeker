import { env } from 'cloudflare:workers';
import { runInDurableObject } from 'cloudflare:test';
import { describe, expect, it } from 'vitest';
import worker from '../src/index';
import { DEFAULT_TTL_SECONDS, ENVELOPE_TABLE_DDL, MAX_TTL_SECONDS, isValidPairingId } from '../src/protocol';

const PAIRING = 'p_7Fq2mXk9LtVbN3wR';
const AUTH = { authorization: 'Bearer test-pairing-token' };

const call = (path: string, init?: RequestInit) =>
  worker.fetch(new Request(`https://relay.example${path}`, init), env as never);

describe('health', () => {
  it('responds 200 without a credential', async () => {
    const res = await call('/v1/health');
    expect(res.status).toBe(200);
    expect(await res.json()).toMatchObject({ ok: true, protocol: 1 });
  });

  it('leaks nothing about any pairing', async () => {
    const body = await (await call('/v1/health')).text();
    expect(body).not.toContain('p_');
  });

  it('refuses non-GET', async () => {
    expect((await call('/v1/health', { method: 'POST' })).status).toBe(405);
  });
});

describe('authorization', () => {
  // Auth is checked before route dispatch on purpose. If 501 came back before 401,
  // an unauthenticated caller could enumerate which routes exist, and P1 would
  // inherit an auth gap with no test covering it.
  it.each(['push', 'pull', 'live'])('%s requires a bearer token', async (route) => {
    const res = await call(`/v1/${PAIRING}/${route}`, { method: route === 'push' ? 'POST' : 'GET' });
    expect(res.status).toBe(401);
  });

  it('rejects an empty bearer token', async () => {
    const res = await call(`/v1/${PAIRING}/pull`, { headers: { authorization: 'Bearer ' } });
    expect(res.status).toBe(401);
  });

  it('does not reveal route existence before authenticating', async () => {
    const real = await call(`/v1/${PAIRING}/pull`);
    const fake = await call(`/v1/${PAIRING}/nonexistent`);
    expect(real.status).toBe(fake.status);
  });
});

describe('routing', () => {
  it('rejects a malformed pairing id', async () => {
    const res = await call('/v1/not-a-pairing/pull', { headers: AUTH });
    expect(res.status).toBe(404);
    expect(await res.json()).toMatchObject({ error: 'pairing_unknown' });
  });

  it('accepts a well-formed pairing id', () => {
    expect(isValidPairingId(PAIRING)).toBe(true);
    expect(isValidPairingId('p_short')).toBe(false);
    expect(isValidPairingId('brandon@example.com')).toBe(false);
  });

  it('404s an unknown route', async () => {
    expect((await call(`/v1/${PAIRING}/wat`, { headers: AUTH })).status).toBe(404);
  });

  it.each([
    ['push', 'POST'],
    ['pull', 'GET'],
  ])('%s is declared but returns 501 in P0', async (route, method) => {
    const res = await call(`/v1/${PAIRING}/${route}`, { method, headers: AUTH });
    expect(res.status).toBe(501);
    expect(await res.json()).toMatchObject({ error: 'not_implemented' });
  });

  it('live requires a websocket upgrade', async () => {
    expect((await call(`/v1/${PAIRING}/live`, { headers: AUTH })).status).toBe(426);
    const upgraded = await call(`/v1/${PAIRING}/live`, { headers: { ...AUTH, upgrade: 'websocket' } });
    expect(upgraded.status).toBe(501);
  });

  it('unpair is declared but returns 501 in P0', async () => {
    const res = await call(`/v1/${PAIRING}`, { method: 'DELETE', headers: AUTH });
    expect(res.status).toBe(501);
  });

  it('rejects the wrong method on a known route', async () => {
    expect((await call(`/v1/${PAIRING}/push`, { method: 'GET', headers: AUTH })).status).toBe(405);
  });

  it('sets no-store on responses', async () => {
    expect((await call('/v1/health')).headers.get('cache-control')).toBe('no-store');
  });
});

describe('PairingChannel durable object', () => {
  const stub = () => env.PAIRING.get(env.PAIRING.idFromName(PAIRING));

  it('instantiates and creates its schema', async () => {
    await runInDurableObject(stub(), async (_instance, state) => {
      const tables = state.storage.sql
        .exec<{ name: string }>("SELECT name FROM sqlite_master WHERE type='table' AND name='envelopes'")
        .toArray();
      expect(tables).toHaveLength(1);
    });
  });

  it('starts empty in both directions', async () => {
    await runInDurableObject(stub(), async (instance) => {
      expect(instance.depth()).toEqual({ e2p: 0, p2e: 0 });
    });
  });

  it('clamps retention to the 30-day spec ceiling', async () => {
    await runInDurableObject(stub(), async (instance) => {
      expect(instance.ttlSeconds(60)).toBe(60);
      expect(instance.ttlSeconds(MAX_TTL_SECONDS * 10)).toBe(MAX_TTL_SECONDS);
      expect(instance.ttlSeconds(0)).toBe(DEFAULT_TTL_SECONDS);
      expect(instance.ttlSeconds(-1)).toBe(DEFAULT_TTL_SECONDS);
      expect(instance.ttlSeconds(Number.NaN)).toBe(DEFAULT_TTL_SECONDS);
    });
  });

  it('purges expired rows and leaves unexpired ones', async () => {
    await runInDurableObject(stub(), async (instance, state) => {
      const now = 1_800_000_000;
      state.storage.sql.exec(
        'INSERT INTO envelopes (dir, seq, ts, key_id, nonce, ciphertext, size, expires_at) VALUES (?,?,?,?,?,?,?,?)',
        'e2p', 1, '2026-06-11T14:02:11Z', 'k-1', 'nonce', new Uint8Array([1, 2, 3]), 3, now - 1,
      );
      state.storage.sql.exec(
        'INSERT INTO envelopes (dir, seq, ts, key_id, nonce, ciphertext, size, expires_at) VALUES (?,?,?,?,?,?,?,?)',
        'e2p', 2, '2026-06-11T14:02:12Z', 'k-1', 'nonce', new Uint8Array([4, 5, 6]), 3, now + 1000,
      );

      expect(instance.purgeExpired(now)).toBe(1);
      expect(instance.depth().e2p).toBe(1);
    });
  });

  it('unpair removes everything', async () => {
    await runInDurableObject(stub(), async (instance) => {
      expect(instance.purgeAll()).toBeGreaterThanOrEqual(0);
      expect(instance.depth()).toEqual({ e2p: 0, p2e: 0 });
    });
  });
});

// These assert the property the product is sold on. They are cheap and they fail loudly
// if someone later adds a column that would make the relay non-blind.
describe('blindness invariants', () => {
  it('the schema stores no identity, only metadata and opaque bytes', () => {
    // Parse real column names rather than substring-matching the DDL text. The naive
    // version flags "ciphertext" for containing "ip" -- a false positive that gets a
    // useful invariant deleted rather than fixed.
    const columns = [...ENVELOPE_TABLE_DDL.matchAll(/^\s+(\w+)\s+(?:TEXT|INTEGER|BLOB)\s/gm)]
      .map((m) => m[1]!.toLowerCase());

    // Exact equality, not a denylist: any NEW column fails this test and forces a
    // deliberate look at whether it makes the relay less blind. A denylist only
    // catches the identity fields somebody already thought of.
    expect(columns).toEqual(['dir', 'seq', 'ts', 'key_id', 'nonce', 'ciphertext', 'size', 'expires_at']);

    for (const identityField of ['email', 'user', 'account', 'username', 'address', 'ip', 'plaintext', 'subject', 'body', 'device']) {
      expect(columns).not.toContain(identityField);
    }
  });

  it('the relay source contains no decryption path', async () => {
    // The relay holds no key material, so any crypto primitive appearing here would be
    // either dead code or a mistake. Asserting on source is crude, but this is exactly
    // the kind of regression that reads as innocuous in a diff.
    const sources = await Promise.all(
      ['../src/index.ts', '../src/channel.ts', '../src/protocol.ts'].map(async (p) => {
        const mod = await import(/* @vite-ignore */ p + '?raw');
        return String(mod.default ?? '');
      }),
    );
    for (const src of sources) {
      expect(src).not.toMatch(/subtle\s*\.\s*decrypt/i);
      expect(src).not.toMatch(/importKey/i);
    }
  });

  it('retention can never exceed the spec ceiling', () => {
    expect(MAX_TTL_SECONDS).toBe(30 * 24 * 60 * 60);
    expect(DEFAULT_TTL_SECONDS).toBeLessThanOrEqual(MAX_TTL_SECONDS);
  });
});
