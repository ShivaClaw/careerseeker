import { env } from 'cloudflare:workers';
import { runInDurableObject } from 'cloudflare:test';
import { describe, expect, it } from 'vitest';
import worker from '../src/index';
import {
  DEFAULT_TTL_SECONDS,
  ENVELOPE_TABLE_DDL,
  MAX_CIPHERTEXT_B64U_CHARS,
  MAX_ENVELOPE_BYTES,
  MAX_PUSH_BODY_CHARS,
  MAX_TTL_SECONDS,
  isValidPairingId,
} from '../src/protocol';

// Each test uses a fresh pairing id so Durable Object state never bleeds between cases.
// The id must be `p_` + exactly 16 base64url chars (isValidPairingId).
let counter = 0;
const freshPairing = () => `p_${String(counter++).padStart(6, '0')}TestVbN3Wx`; // 6 + 10 = 16

const call = (path: string, init?: RequestInit) =>
  worker.fetch(new Request(`https://relay.example${path}`, init), env as never);

const bearer = (token: string) => ({ authorization: `Bearer ${token}` });

/** Bootstrap a pairing channel with a known token, the way the engine does via /create. */
async function bootstrap(token: string): Promise<string> {
  const pairing = freshPairing();
  const res = await call(`/v1/${pairing}/create`, { method: 'POST', headers: bearer(token) });
  expect(res.status).toBe(201);
  return pairing;
}

/** A minimal well-formed envelope. `ciphertext` is opaque text; the relay never reads it. */
const envelope = (dir: string, seq: number, extra: Record<string, unknown> = {}) => JSON.stringify({
  v: 1, pairing: 'p_x', dir, seq, ts: '2026-06-11T14:02:11Z', key_id: 'k-1',
  nonce: 'AAAAAAAAAAAAAAAA', ciphertext: `opaque-${dir}-${seq}`, ...extra,
});

describe('health', () => {
  it('responds 200 without a credential', async () => {
    const res = await call('/v1/health');
    expect(res.status).toBe(200);
    expect(await res.json()).toMatchObject({ ok: true, protocol: 1 });
  });

  it('leaks nothing about any pairing', async () => {
    expect(await (await call('/v1/health')).text()).not.toContain('p_');
  });

  it('refuses non-GET', async () => {
    expect((await call('/v1/health', { method: 'POST' })).status).toBe(405);
  });
});

describe('routing and auth', () => {
  it('rejects a malformed pairing id', async () => {
    const res = await call('/v1/not-a-pairing/pull', { headers: bearer('t') });
    expect(res.status).toBe(404);
    expect(await res.json()).toMatchObject({ error: 'pairing_unknown' });
  });

  it('validates pairing id shape', () => {
    expect(isValidPairingId('p_7Fq2mXk9LtVbN3wR')).toBe(true);
    expect(isValidPairingId('p_short')).toBe(false);
    expect(isValidPairingId('brandon@example.com')).toBe(false);
  });

  it.each(['push', 'pull', 'live'])('%s requires a bearer token', async (route) => {
    const res = await call(`/v1/${freshPairing()}/${route}`, { method: route === 'push' ? 'POST' : 'GET' });
    expect(res.status).toBe(401);
  });

  it('rejects an empty bearer token', async () => {
    const res = await call(`/v1/${freshPairing()}/pull`, { headers: { authorization: 'Bearer ' } });
    expect(res.status).toBe(401);
  });

  it('does not reveal route existence before authenticating', async () => {
    const pairing = freshPairing();
    const real = await call(`/v1/${pairing}/pull`);
    const fake = await call(`/v1/${pairing}/nonexistent`);
    expect(real.status).toBe(fake.status); // both 401 — no route map leaks
  });

  it('sets no-store on responses', async () => {
    expect((await call('/v1/health')).headers.get('cache-control')).toBe('no-store');
  });
});

describe('transport response bodies, every route (§2.3)', () => {
  // §2.2 pinned `push` and said in terms that v1 pinned "no other route's". These pin the
  // rest, measured the same way. They are DESCRIPTIVE: every expectation below was read off
  // the running Worker first and written down second, so none of them tightens what the
  // relay refuses. That direction matters — §3.1's amendment forbids the relay refusing an
  // envelope the document declares legal, and a test written from the spec downwards is how
  // that bug got in.

  it('emits exactly nine transport codes, and `exists` is one of them', async () => {
    // PQ-S2-3's table listed eight and dropped `exists`; §2.2's prose inherited the gap.
    // The set is asserted here so the document and the Worker cannot drift apart silently.
    const seen = new Set<string>();
    const record = async (res: Response) => {
      const body = await res.clone().json().catch(() => ({}));
      const code = (body as { error?: string }).error;
      if (code) seen.add(code);
    };

    const p = await bootstrap('tok');
    await record(await call('/v1/health', { method: 'POST' }));                       // method_not_allowed
    await record(await call('/v1'));                                                  // not_found
    await record(await call('/v1/not-a-pairing/pull'));                               // pairing_unknown
    await record(await call(`/v1/${freshPairing()}/pull`));                           // unauthorized
    await record(await call(`/v1/${p}/create`, { method: 'POST', headers: bearer('tok') })); // exists
    await record(await call(`/v1/${p}/pull`, { headers: bearer('tok') }));            // bad_request
    await record(await call(`/v1/${p}/live`, { headers: bearer('tok') }));            // upgrade_required
    await record(await call(`/v1/${p}/push`, {
      method: 'POST', headers: bearer('tok'), body: 'x'.repeat(MAX_PUSH_BODY_CHARS + 1),
    }));                                                                              // too_large
    await call(`/v1/${p}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 5) });
    await record(await call(`/v1/${p}/push`, {
      method: 'POST', headers: bearer('tok'), body: envelope('e2p', 5),
    }));                                                                              // replay_rejected

    expect([...seen].sort()).toEqual([
      'bad_request', 'exists', 'method_not_allowed', 'not_found', 'pairing_unknown',
      'replay_rejected', 'too_large', 'unauthorized', 'upgrade_required',
    ]);
  });

  it('`pairing_unknown` means the id is MALFORMED, never that the pairing is unknown', async () => {
    // The name describes a condition it is never emitted for. A well-formed id that was
    // never created does not get it; only a shape violation does.
    const malformed = await call('/v1/not-a-pairing/pull', { headers: bearer('tok') });
    expect(malformed.status).toBe(404);
    expect(await malformed.json()).toMatchObject({ error: 'pairing_unknown' });

    const neverCreated = await call(`/v1/${freshPairing()}/pull?dir=e2p&since=0`, { headers: bearer('tok') });
    expect(neverCreated.status).toBe(401);
    expect(await neverCreated.json()).toMatchObject({ error: 'unauthorized' });
  });

  it('after unpair every route answers 401, not 404 — §7.2\'s `pairing_unknown` condition', async () => {
    // §7.2 defines the PAYLOAD code `pairing_unknown` as "the relay has no Durable Object for
    // this pairing". This is that exact condition, and the transport reports it as 401
    // `unauthorized` on every route. Nothing the relay emits distinguishes "you were unpaired"
    // from "your token is wrong". PQ-S2-4 records what that costs the phone.
    const p = await bootstrap('tok');
    expect((await call(`/v1/${p}`, { method: 'DELETE', headers: bearer('tok') })).status).toBe(200);

    for (const [path, init] of [
      [`/v1/${p}/pull?dir=e2p&since=0`, { headers: bearer('tok') }],
      [`/v1/${p}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 1) }],
      [`/v1/${p}/pair`, { headers: bearer('tok') }],
      [`/v1/${p}`, { method: 'DELETE', headers: bearer('tok') }],
    ] as const) {
      const res = await call(path, init as RequestInit);
      expect(res.status, `${path} after unpair`).toBe(401);
      expect(await res.json()).toMatchObject({ error: 'unauthorized' });
    }
  });

  it('unpair is not a tombstone: the same pairing id re-bootstraps', async () => {
    // Why 401 is defensible rather than merely convenient — a purged pairing is
    // indistinguishable from one that never existed, which is the property that stops the
    // relay answering "did this pairing ever exist?" to anyone holding a wrong token.
    const p = await bootstrap('tok');
    await call(`/v1/${p}`, { method: 'DELETE', headers: bearer('tok') });
    const again = await call(`/v1/${p}/create`, { method: 'POST', headers: bearer('different-token') });
    expect(again.status).toBe(201);
  });

  it('GET /pair answers 404 `not_found` for the ordinary "nothing waiting" case', async () => {
    // Both before a completion is posted and after the one-shot read. This is a transient,
    // expected state — not a statement about the pairing's existence.
    const p = await bootstrap('tok');
    const before = await call(`/v1/${p}/pair`, { headers: bearer('tok') });
    expect(before.status).toBe(404);
    expect(await before.json()).toMatchObject({ error: 'not_found' });

    const completion = JSON.stringify({ suite: 's', phone_pub: 'p', nonce: 'n', ciphertext: 'c' });
    await call(`/v1/${p}/pair`, { method: 'POST', headers: bearer('tok'), body: completion });
    const taken = await call(`/v1/${p}/pair`, { headers: bearer('tok') });
    expect(taken.status).toBe(200);
    // The stored body is returned RAW — not wrapped in {ok:…}. The engine reads it as the
    // completion document itself (RelayClient.cs TakeCompletionAsync returns the string).
    expect(await taken.text()).toBe(completion);

    const after = await call(`/v1/${p}/pair`, { headers: bearer('tok') });
    expect(after.status).toBe(404);
    expect(await after.json()).toMatchObject({ error: 'not_found' });
  });

  it('409 carries three different bodies across the three routes that emit it', async () => {
    // Status alone is not enough to act on: only push's 409 carries a number.
    const p = await bootstrap('tok');
    const createConflict = await call(`/v1/${p}/create`, { method: 'POST', headers: bearer('tok') });
    expect(createConflict.status).toBe(409);
    expect(await createConflict.json()).toEqual({ error: 'exists' });

    const completion = JSON.stringify({ suite: 's', phone_pub: 'p', nonce: 'n', ciphertext: 'c' });
    await call(`/v1/${p}/pair`, { method: 'POST', headers: bearer('tok'), body: completion });
    const pairConflict = await call(`/v1/${p}/pair`, { method: 'POST', headers: bearer('tok'), body: completion });
    expect(pairConflict.status).toBe(409);
    expect(await pairConflict.json()).toEqual({ error: 'exists' });

    await call(`/v1/${p}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 7) });
    const pushConflict = await call(`/v1/${p}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 7) });
    expect(pushConflict.status).toBe(409);
    expect(await pushConflict.json()).toEqual({ error: 'replay_rejected', latest: 7 });
  });

  it('create answers {ok:true} on 201 and {ok:true,rotated:true} on a rotation', async () => {
    const p = await bootstrap('provisional');
    const finalHash = [...new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode('final')))]
      .map((b) => b.toString(16).padStart(2, '0')).join('');
    const rot = await call(`/v1/${p}/create`, {
      method: 'POST', headers: bearer('provisional'), body: JSON.stringify({ rotate_to: finalHash }),
    });
    expect(rot.status).toBe(200);
    expect(await rot.json()).toEqual({ ok: true, rotated: true });
  });

  it('DELETE answers {ok:true,purged:N} and N counts envelopes, not storage keys', async () => {
    const p = await bootstrap('tok');
    await call(`/v1/${p}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 1) });
    await call(`/v1/${p}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('p2e', 1) });
    const res = await call(`/v1/${p}`, { method: 'DELETE', headers: bearer('tok') });
    expect(res.status).toBe(200);
    expect(await res.json()).toEqual({ ok: true, purged: 2 });
  });

  it('405 is reachable only for a route that exists with a method it does not take', async () => {
    const p = await bootstrap('tok');
    const wrongMethod = await call(`/v1/${p}/pull`, { method: 'DELETE', headers: bearer('tok') });
    expect(wrongMethod.status).toBe(405);
    expect(await wrongMethod.json()).toMatchObject({ error: 'method_not_allowed' });

    // An unknown route under a valid credential is 404, not 405 — the distinction only
    // exists for a caller that already authenticated.
    const unknownRoute = await call(`/v1/${p}/nonsense`, { headers: bearer('tok') });
    expect(unknownRoute.status).toBe(404);
    expect(await unknownRoute.json()).toMatchObject({ error: 'not_found' });
  });

  it('live answers 426 without an upgrade header and 400 for a bad dir', async () => {
    const p = await bootstrap('tok');
    const noUpgrade = await call(`/v1/${p}/live?dir=e2p`, { headers: bearer('tok') });
    expect(noUpgrade.status).toBe(426);
    expect(await noUpgrade.json()).toMatchObject({ error: 'upgrade_required' });

    const badDir = await call(`/v1/${p}/live?dir=sideways`, {
      headers: { ...bearer('tok'), upgrade: 'websocket' },
    });
    expect(badDir.status).toBe(400);
    expect(await badDir.json()).toMatchObject({ error: 'bad_request' });
  });

  it('every transport error body is exactly {error} — only push\'s 409 carries a second field', async () => {
    // The rule a client can rely on: read the status, and read `latest` only on a 409 from
    // push. Nothing else in the transport namespace carries actionable data.
    const p = await bootstrap('tok');
    const errors = await Promise.all([
      call('/v1', {}),
      call(`/v1/${p}/pull`, { headers: bearer('tok') }),
      call(`/v1/${p}/live`, { headers: bearer('tok') }),
      call(`/v1/${p}/nonsense`, { headers: bearer('tok') }),
      call(`/v1/${p}/create`, { method: 'POST', headers: bearer('tok') }),
    ]);
    for (const res of errors) {
      expect(Object.keys((await res.json()) as object)).toEqual(['error']);
    }
  });
});

describe('bootstrap and token rotation (§5.2.1, §5.2.3)', () => {
  it('create registers the token; a second create without rotate_to is 409', async () => {
    const pairing = await bootstrap('provisional-token');
    const again = await call(`/v1/${pairing}/create`, { method: 'POST', headers: bearer('provisional-token') });
    expect(again.status).toBe(409);
  });

  it('a wrong bearer cannot touch an existing channel', async () => {
    const pairing = await bootstrap('right-token');
    const res = await call(`/v1/${pairing}/create`, { method: 'POST', headers: bearer('wrong-token') });
    expect(res.status).toBe(401);
  });

  it('rotates provisional -> final one-way, and the new token then authorizes', async () => {
    const pairing = await bootstrap('provisional');
    // SHA-256("final-token") hex.
    const finalHash = [...new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode('final-token')))]
      .map((b) => b.toString(16).padStart(2, '0')).join('');
    const rot = await call(`/v1/${pairing}/create`, {
      method: 'POST', headers: bearer('provisional'), body: JSON.stringify({ rotate_to: finalHash }),
    });
    expect(rot.status).toBe(200);

    // Old token is dead; new token works.
    expect((await call(`/v1/${pairing}/pull?dir=e2p&since=0`, { headers: bearer('provisional') })).status).toBe(401);
    expect((await call(`/v1/${pairing}/pull?dir=e2p&since=0`, { headers: bearer('final-token') })).status).toBe(200);
  });

  it('rejects a non-hex rotate_to', async () => {
    const pairing = await bootstrap('tok');
    const res = await call(`/v1/${pairing}/create`, {
      method: 'POST', headers: bearer('tok'), body: JSON.stringify({ rotate_to: 'nothex' }),
    });
    expect(res.status).toBe(400);
  });
});

describe('pairing completion (§5.2.2)', () => {
  const completion = JSON.stringify({ suite: 'p256-hkdf-sha256', phone_pub: 'AAA', nonce: 'BBB', ciphertext: 'CCC' });

  it('stores once and is one-shot on read', async () => {
    const pairing = await bootstrap('tok');
    expect((await call(`/v1/${pairing}/pair`, { method: 'POST', headers: bearer('tok'), body: completion })).status).toBe(201);

    const got = await call(`/v1/${pairing}/pair`, { headers: bearer('tok') });
    expect(got.status).toBe(200);
    expect(await got.json()).toMatchObject({ suite: 'p256-hkdf-sha256' });

    // Deleted on read: a replayed collection gets nothing.
    expect((await call(`/v1/${pairing}/pair`, { headers: bearer('tok') })).status).toBe(404);
  });

  it('refuses a second completion (409)', async () => {
    const pairing = await bootstrap('tok');
    await call(`/v1/${pairing}/pair`, { method: 'POST', headers: bearer('tok'), body: completion });
    const second = await call(`/v1/${pairing}/pair`, { method: 'POST', headers: bearer('tok'), body: completion });
    expect(second.status).toBe(409);
  });

  it('rejects an incomplete completion body', async () => {
    const pairing = await bootstrap('tok');
    const res = await call(`/v1/${pairing}/pair`, { method: 'POST', headers: bearer('tok'), body: JSON.stringify({ suite: 'x' }) });
    expect(res.status).toBe(400);
  });
});

describe('push / pull envelope flow', () => {
  it('pushes and pulls back the exact ciphertext bytes', async () => {
    const pairing = await bootstrap('tok');
    expect((await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 1) })).status).toBe(201);
    expect((await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 2) })).status).toBe(201);

    const res = await call(`/v1/${pairing}/pull?dir=e2p&since=0`, { headers: bearer('tok') });
    expect(res.status).toBe(200);
    const body = await res.json() as { envelopes: unknown[]; latest: number };
    expect(body.envelopes).toHaveLength(2);
    expect(body.latest).toBe(2);
  });

  it('pull?since= returns only newer envelopes', async () => {
    const pairing = await bootstrap('tok');
    for (const s of [1, 2, 3]) await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', s) });
    const body = await (await call(`/v1/${pairing}/pull?since=2&dir=e2p`, { headers: bearer('tok') })).json() as { envelopes: unknown[] };
    expect(body.envelopes).toHaveLength(1);
  });

  it('directions are independent queues', async () => {
    const pairing = await bootstrap('tok');
    await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 1) });
    await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('p2e', 1) });
    const e2p = await (await call(`/v1/${pairing}/pull?dir=e2p&since=0`, { headers: bearer('tok') })).json() as { envelopes: unknown[] };
    const p2e = await (await call(`/v1/${pairing}/pull?dir=p2e&since=0`, { headers: bearer('tok') })).json() as { envelopes: unknown[] };
    expect(e2p.envelopes).toHaveLength(1);
    expect(p2e.envelopes).toHaveLength(1);
  });

  it('refuses a duplicate or regressed seq (409)', async () => {
    const pairing = await bootstrap('tok');
    await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 5) });
    const dup = await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 5) });
    expect(dup.status).toBe(409);
    const back = await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 3) });
    expect(back.status).toBe(409);
  });

  it('rejects a malformed envelope header (400)', async () => {
    const pairing = await bootstrap('tok');
    const res = await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: JSON.stringify({ v: 1, dir: 'sideways' }) });
    expect(res.status).toBe(400);
  });

  // §3.1 caps the DECODED ciphertext at MAX_ENVELOPE_BYTES. The relay cannot decode, so its
  // guard counts base64url characters and the two constants must stay in step. Until
  // 2026-08-09 this suite asserted `1 MiB + 1 chars → 413`, which pinned a character count
  // against a byte budget and quietly capped the decoded payload at 786,432 bytes.
  it('derives the character cap from the byte cap, not from a second round number', () => {
    expect(MAX_CIPHERTEXT_B64U_CHARS).toBe(Math.ceil((MAX_ENVELOPE_BYTES * 4) / 3));
    expect(MAX_CIPHERTEXT_B64U_CHARS).toBe(1398102);
    // The old guard would have refused this much legal payload.
    expect(MAX_ENVELOPE_BYTES - Math.floor(MAX_ENVELOPE_BYTES / 4) * 3).toBe(256 * 1024);
  });

  it('carries the largest ciphertext the protocol declares legal (201)', async () => {
    const pairing = await bootstrap('tok');
    const atCap = envelope('e2p', 1, { ciphertext: 'A'.repeat(MAX_CIPHERTEXT_B64U_CHARS) });
    const res = await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: atCap });
    expect(res.status).toBe(201);
  });

  it('returns a maximum-size envelope through pull byte-for-byte', async () => {
    const pairing = await bootstrap('tok');
    const atCap = envelope('e2p', 1, { ciphertext: 'A'.repeat(MAX_CIPHERTEXT_B64U_CHARS) });
    await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: atCap });
    // Read as TEXT, not JSON: pull splices the stored envelope into its response verbatim,
    // so this is the assertion that actually proves storage did not truncate a row this size.
    const body = await (await call(`/v1/${pairing}/pull?dir=e2p&since=0`, { headers: bearer('tok') })).text();
    expect(body).toBe(`{"envelopes":[${atCap}],"latest":1}`);
  });

  it('rejects one character beyond the declared maximum (413)', async () => {
    const pairing = await bootstrap('tok');
    const over = envelope('e2p', 1, { ciphertext: 'A'.repeat(MAX_CIPHERTEXT_B64U_CHARS + 1) });
    const res = await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: over });
    expect(res.status).toBe(413);
    expect(await res.json()).toMatchObject({ error: 'too_large' });
  });

  it('rejects an over-long body before parsing it (413)', async () => {
    const pairing = await bootstrap('tok');
    // Not even valid JSON: the body guard must fire first, so nothing this large is parsed.
    const res = await call(`/v1/${pairing}/push`, {
      method: 'POST', headers: bearer('tok'), body: 'A'.repeat(MAX_PUSH_BODY_CHARS + 1),
    });
    expect(res.status).toBe(413);
  });

  it('pull requires a valid dir', async () => {
    const pairing = await bootstrap('tok');
    expect((await call(`/v1/${pairing}/pull?since=0`, { headers: bearer('tok') })).status).toBe(400);
  });
});

describe('unpair', () => {
  it('DELETE purges the queue and the token', async () => {
    const pairing = await bootstrap('tok');
    await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 1) });
    expect((await call(`/v1/${pairing}`, { method: 'DELETE', headers: bearer('tok') })).status).toBe(200);
    // Token gone → the channel no longer authorizes anyone.
    expect((await call(`/v1/${pairing}/pull?dir=e2p&since=0`, { headers: bearer('tok') })).status).toBe(401);
  });
});

describe('PairingChannel durable object internals', () => {
  const stubFor = async (token: string) => {
    const pairing = await bootstrap(token);
    return env.PAIRING.get(env.PAIRING.idFromName(pairing));
  };

  it('creates its schema and starts empty', async () => {
    await runInDurableObject(await stubFor('tok'), async (instance, state) => {
      const tables = state.storage.sql
        .exec<{ name: string }>("SELECT name FROM sqlite_master WHERE type='table' AND name='envelopes'").toArray();
      expect(tables).toHaveLength(1);
      expect(instance.depth()).toEqual({ e2p: 0, p2e: 0 });
    });
  });

  it('clamps retention to the 30-day spec ceiling', async () => {
    await runInDurableObject(await stubFor('tok'), async (instance) => {
      expect(instance.ttlSeconds(60)).toBe(60);
      expect(instance.ttlSeconds(MAX_TTL_SECONDS * 10)).toBe(MAX_TTL_SECONDS);
      expect(instance.ttlSeconds(0)).toBe(DEFAULT_TTL_SECONDS);
      expect(instance.ttlSeconds(-1)).toBe(DEFAULT_TTL_SECONDS);
    });
  });

  it('purgeExpired removes only expired rows', async () => {
    await runInDurableObject(await stubFor('tok'), async (instance, state) => {
      const now = 1_800_000_000;
      state.storage.sql.exec(
        'INSERT INTO envelopes (dir, seq, ts, key_id, nonce, ciphertext, size, expires_at) VALUES (?,?,?,?,?,?,?,?)',
        'e2p', 1, 't', 'k', 'n', 'expired', 7, now - 1);
      state.storage.sql.exec(
        'INSERT INTO envelopes (dir, seq, ts, key_id, nonce, ciphertext, size, expires_at) VALUES (?,?,?,?,?,?,?,?)',
        'e2p', 2, 't', 'k', 'n', 'fresh', 5, now + 1000);
      expect(instance.purgeExpired(now)).toBe(1);
      expect(instance.depth().e2p).toBe(1);
    });
  });

  // The property the product is sold on, proven rather than asserted: dump every stored
  // row and confirm nothing in it is readable structured data. `ciphertext` holds opaque
  // bytes; no column parses as the envelope JSON, because the relay never stored plaintext.
  it('stored rows contain ciphertext only — no readable content', async () => {
    const stub = await stubFor('tok');
    await runInDurableObject(stub, async (instance, state) => {
      // Simulate what a real push stores: the whole envelope JSON, whose `ciphertext`
      // field is itself opaque. The relay cannot and does not separate out plaintext.
      state.storage.sql.exec(
        'INSERT INTO envelopes (dir, seq, ts, key_id, nonce, ciphertext, size, expires_at) VALUES (?,?,?,?,?,?,?,?)',
        'e2p', 1, '2026-06-11T14:02:11Z', 'k-1', 'AAAAAAAAAAAAAAAA', 'ciphertext-opaque-bytes', 23, 9_999_999_999);

      const cols = state.storage.sql
        .exec<{ name: string }>("SELECT name FROM pragma_table_info('envelopes')").toArray().map((r) => r.name);
      // Exact schema — any NEW column forces a deliberate look at whether it de-blinds the relay.
      expect(cols).toEqual(['dir', 'seq', 'ts', 'key_id', 'nonce', 'ciphertext', 'size', 'expires_at']);

      for (const forbidden of ['email', 'user', 'account', 'address', 'plaintext', 'subject', 'body', 'device']) {
        expect(cols).not.toContain(forbidden);
      }
    });
  });
});

describe('blindness invariants', () => {
  it('the schema names no identity column', () => {
    const cols = [...ENVELOPE_TABLE_DDL.matchAll(/^\s+(\w+)\s+(?:TEXT|INTEGER|BLOB)\s/gm)].map((m) => m[1]!.toLowerCase());
    expect(cols).toEqual(['dir', 'seq', 'ts', 'key_id', 'nonce', 'ciphertext', 'size', 'expires_at']);
  });

  it('retention can never exceed the spec ceiling', () => {
    expect(MAX_TTL_SECONDS).toBe(30 * 24 * 60 * 60);
    expect(DEFAULT_TTL_SECONDS).toBeLessThanOrEqual(MAX_TTL_SECONDS);
  });
});
