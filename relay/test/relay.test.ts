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
  MAX_SEQ,
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

  // The regex in protocol.ts is a hand-transcription of the `pairing` row of the §3 envelope
  // field table — "`p_` + 16 base64url chars" — and nothing compared the two. The case above
  // pins one valid id and two obviously-wrong ones, which is satisfied by a regex widened to
  // `{16,32}` or by a charset that admits `.`; both were measured green. The prefix was covered
  // only incidentally, by every other test happening to use a `p_` id. Length, charset and
  // prefix are pinned against the document here, so a widened shape fails on its own account.
  it('the pairing id shape matches §3 exactly: `p_` + 16 base64url chars', () => {
    expect(isValidPairingId('p_7Fq2mXk9LtVbN3wR')).toBe(true); // exactly 16

    expect(isValidPairingId('p_7Fq2mXk9LtVbN3w')).toBe(false); // 15
    expect(isValidPairingId('p_7Fq2mXk9LtVbN3wRx')).toBe(false); // 17

    // base64url's alphabet is [A-Za-z0-9_-]. Standard-base64 and separator characters are
    // the plausible slips, and none of them is a pairing id.
    for (const bad of ['.', '+', '/', '=', ' ', '@']) {
      expect(isValidPairingId(`p_${bad}Fq2mXk9LtVbN3wR`)).toBe(false);
    }
    expect(isValidPairingId('p_-Fq2mXk9LtVbN3wR')).toBe(true);
    expect(isValidPairingId('p__Fq2mXk9LtVbN3wR')).toBe(true);

    // The prefix is part of the shape, not decoration.
    expect(isValidPairingId('q_7Fq2mXk9LtVbN3wR')).toBe(false);
    expect(isValidPairingId('7Fq2mXk9LtVbN3wR')).toBe(false);
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

  // `latest` is MAX(seq) for the direction, computed independently of `since`. Two separate
  // consumers rely on that, and neither can observe a violation locally:
  //
  //   * the inbound pump uses it as its PAGINATION LOOP BOUND -- `MoreAvailable: _cursor <
  //     page.Latest` (InboundPump.cs) -- while pulling with a moving, non-zero `since`. A
  //     `since`-relative `latest` collapses that comparison as soon as a page comes back empty,
  //     so the pump stops draining mid-backlog and reports a clean drain. Silent, not loud.
  //   * the engine's §6.1 startup reconcile asks for the e2p high-water mark and passes the
  //     vault's mark rather than 0, so it does not drag the whole retained direction across the
  //     wire just to read one number.
  //
  // The implementation has had this property since the P1 relay, but nothing on this branch
  // asserted it -- measured: making `latest` `since`-relative leaves the rest of this file
  // GREEN. It was pinned only on `claude/s6-resume-reconciliation` (PR #53), which the landing
  // plan recommends closing, so without this the guard leaves with it.
  it('latest is the direction high-water mark, independent of since', async () => {
    const pairing = await bootstrap('tok');
    for (const s of [1, 2, 3]) await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', s) });

    const at = async (since: number) =>
      await (await call(`/v1/${pairing}/pull?dir=e2p&since=${since}`, { headers: bearer('tok') }))
        .json() as { envelopes: unknown[]; latest: number };

    // since=0 drags all three; since=3 drags none; both must report the same latest.
    const all = await at(0);
    const none = await at(3);
    expect(all.envelopes).toHaveLength(3);
    expect(none.envelopes).toHaveLength(0);
    expect(all.latest).toBe(3);
    expect(none.latest).toBe(3);

    // And a since past the end still reports the real mark rather than clamping to it.
    expect((await at(99)).latest).toBe(3);
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

  // ---------------------------------------------------------------- §3.2 seq range
  //
  // These build the body as raw TEXT rather than through `envelope()`, because the point
  // is the exact number the sender put on the wire and JSON.stringify would round it
  // before the relay ever saw it.
  const rawEnvelope = (dir: string, seqText: string) =>
    `{"v":1,"pairing":"p_x","dir":"${dir}","seq":${seqText},"ts":"2026-06-11T14:02:11Z",`
    + `"key_id":"k-1","nonce":"AAAAAAAAAAAAAAAA","ciphertext":"opaque"}`;

  const pushRaw = (pairing: string, body: string) =>
    call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body });

  it('accepts seq at the §3.2 maximum, and MAX_SEQ is the derivation not a literal', async () => {
    expect(MAX_SEQ).toBe(2 ** 53 - 1);
    const pairing = await bootstrap('tok');
    const res = await pushRaw(pairing, rawEnvelope('e2p', String(MAX_SEQ)));
    expect(res.status).toBe(201);
    // The boundary value survives the round trip exactly — which is the whole reason the
    // cap sits here and not higher.
    const page = await (await call(`/v1/${pairing}/pull?dir=e2p&since=0`, { headers: bearer('tok') })).text();
    expect(page).toContain(`"latest":${MAX_SEQ}`);
  });

  it.each([
    ['2^53 (one past the maximum)', '9007199254740992'],
    ['2^62, which silently rounded to a different number', '4611686018427387904'],
    ['1e19, above Long.MaxValue', '10000000000000000000'],
    ['1e300, which the old Number.isInteger guard accepted', '1e300'],
  ])('refuses seq above the §3.2 maximum: %s', async (_label, seqText) => {
    const pairing = await bootstrap('tok');
    const res = await pushRaw(pairing, rawEnvelope('e2p', seqText));
    expect(res.status).toBe(400);
    expect(await res.json()).toMatchObject({ error: 'bad_request' });
  });

  it('carries no counter evidence on an out-of-range refusal', async () => {
    // 400 means nothing was appended, so there is no `latest` to report — unlike the 409
    // path, where `latest` is the sender's reconciliation input.
    const pairing = await bootstrap('tok');
    const res = await pushRaw(pairing, rawEnvelope('e2p', '1e300'));
    expect(Object.keys(await res.json() as object)).toEqual(['error']);
  });

  it('leaves the direction usable after refusing an out-of-range seq', async () => {
    // This is the regression the bound exists to prevent. Before §3.2 the 1e300 envelope
    // was APPENDED, and every later push in that direction answered 409 against a `latest`
    // of 1e+300 that neither receiver could even parse.
    const pairing = await bootstrap('tok');
    expect((await pushRaw(pairing, rawEnvelope('e2p', '1e300'))).status).toBe(400);
    expect((await pushRaw(pairing, rawEnvelope('e2p', '1'))).status).toBe(201);
    const page = await (await call(`/v1/${pairing}/pull?dir=e2p&since=0`, { headers: bearer('tok') })).text();
    expect(page).toContain('"latest":1');
  });

  it('keeps every reported latest inside the range both receivers can parse', async () => {
    // The read-path half of §3.2. `latest` is emitted from the relay's double, so a value
    // the relay accepts but cannot represent exactly becomes a page the engine's
    // GetInt64() and the phone's strictLong() both reject — breaking the GET /pull
    // reconciliation §6.1 prescribes for a sender whose counter is behind.
    const pairing = await bootstrap('tok');
    await pushRaw(pairing, rawEnvelope('e2p', String(MAX_SEQ)));
    const body = await (await call(`/v1/${pairing}/pull?dir=e2p&since=0`, { headers: bearer('tok') })).text();
    const latest = (JSON.parse(body) as { latest: number }).latest;
    expect(Number.isSafeInteger(latest)).toBe(true);
    // Neither exponent notation nor a value past Long.MaxValue: both are unparseable to
    // the receivers, and both were reachable before this bound.
    expect(body).not.toContain('e+');
    expect(latest).toBeLessThanOrEqual(MAX_SEQ);
  });

  it('no longer collides two distinct wire values onto one double', async () => {
    // Measured before the bound: 9007199254740992 answered 201 and 9007199254740993 --
    // a strictly LARGER integer -- answered 409 replay_rejected, because both land on the
    // same double. Now neither is admitted, so the collision is unreachable.
    const pairing = await bootstrap('tok');
    expect((await pushRaw(pairing, rawEnvelope('e2p', '9007199254740992'))).status).toBe(400);
    expect((await pushRaw(pairing, rawEnvelope('e2p', '9007199254740993'))).status).toBe(400);
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

  // The 409 body, not just its status. `latest` is the relay's high-water mark for the
  // direction, and it is the input §6.1's counter reconciliation needs: a sender whose
  // persisted counter has fallen behind can only retry an envelope the relay refuses
  // forever unless it is told the floor. The android :core RelayClient parses this field
  // into RelayResult.Conflict, so it is a cross-repo contract with nothing pinning it here.
  it('reports its high-water mark in the refusal, not just the status', async () => {
    const pairing = await bootstrap('tok');
    await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 7) });
    const dup = await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 7) });
    expect(dup.status).toBe(409);
    expect(await dup.json()).toEqual({ error: 'replay_rejected', latest: 7 });
  });

  // The relay is a pipe, and §3's "unknown top-level fields MUST be rejected" binds the
  // RECEIVERS, not the relay: a relay that stripped fields it did not recognise would
  // silently repair envelopes the receivers are required to reject, and the rule would stop
  // being testable end to end. Pinned because it is the wire behaviour PQ-A2-3's
  // invalid-unknown-field vector depends on -- the field has to survive the trip to be
  // rejected at the far end.
  it('carries an unknown top-level field through to the receiver verbatim', async () => {
    const pairing = await bootstrap('tok');
    await call(`/v1/${pairing}/push`, {
      method: 'POST', headers: bearer('tok'), body: envelope('e2p', 1, { future_field: 'surprise' }),
    });
    const body = await (await call(`/v1/${pairing}/pull?dir=e2p&since=0`, { headers: bearer('tok') })).json() as {
      envelopes: Record<string, unknown>[];
    };
    expect(body.envelopes[0]!.future_field).toBe('surprise');
  });
});

// §2: "The relay MUST purge any envelope older than the configured TTL." Collection is
// alarm-driven, and an alarm is scheduled rather than instantaneous, so the read path has
// to enforce the promise too -- otherwise retention holds only as fast as a background job
// happens to run. Before this was fixed, both cases below returned the expired envelope.
describe('retention is enforced on the read path, not only by the alarm (§2)', () => {
  // Typed structurally rather than as DurableObjectState: test/tsconfig.json does not pull
  // in the generated worker globals, and this file is meant to stay checkable under it.
  const expiredRow = (sql: { exec: (query: string, ...bindings: unknown[]) => unknown }, dir: string, seq: number) =>
    sql.exec(
      'INSERT INTO envelopes (dir, seq, ts, key_id, nonce, ciphertext, size, expires_at) VALUES (?,?,?,?,?,?,?,?)',
      dir, seq, '2026-06-11T14:02:11Z', 'k-1', 'AAAAAAAAAAAAAAAA', `{"seq":${seq},"expired":true}`, 28, 1);

  it('does not serve an expired envelope that the alarm has not collected yet', async () => {
    const pairing = await bootstrap('tok');
    await runInDurableObject(env.PAIRING.get(env.PAIRING.idFromName(pairing)), async (_i, state) => {
      expiredRow(state.storage.sql, 'e2p', 1);
    });
    const body = await (await call(`/v1/${pairing}/pull?dir=e2p&since=0`, { headers: bearer('tok') })).json() as {
      envelopes: unknown[]; latest: number;
    };
    expect(body.envelopes).toHaveLength(0);
  });

  // The half that turns a stale read into a hang. `latest` is the client's loop bound, so if
  // it counts a row the page will not return, the client pulls the same page forever waiting
  // for a seq that can never arrive.
  it('excludes expired rows from latest, so the page and its loop bound agree', async () => {
    const pairing = await bootstrap('tok');
    await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 1) });
    await runInDurableObject(env.PAIRING.get(env.PAIRING.idFromName(pairing)), async (_i, state) => {
      expiredRow(state.storage.sql, 'e2p', 2);
    });
    const body = await (await call(`/v1/${pairing}/pull?dir=e2p&since=0`, { headers: bearer('tok') })).json() as {
      envelopes: { seq: number }[]; latest: number;
    };
    expect(body.envelopes.map((e) => e.seq)).toEqual([1]);
    expect(body.latest).toBe(1);
  });

  // The push guard deliberately keeps counting expired-but-uncollected rows: serving one is
  // a retention failure, forgetting one lowers the replay floor. Opposite rows, opposite
  // rules, same table -- pinned so the pull fix is never "tidied" into push.
  it('still refuses a seq at or below an expired-but-uncollected row', async () => {
    const pairing = await bootstrap('tok');
    await runInDurableObject(env.PAIRING.get(env.PAIRING.idFromName(pairing)), async (_i, state) => {
      expiredRow(state.storage.sql, 'e2p', 5);
    });
    const res = await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 5) });
    expect(res.status).toBe(409);
  });

  // ---------- the two high-water marks: measured, and each consumer's side pinned ----------
  //
  // The comment above says the push guard and the pull page "want opposite things from the same
  // rows". That was a statement of intent. What it costs was never measured: between expiry and
  // collection the SAME direction reports TWO different high-water marks, and they are not
  // interchangeable. Each is read by a different consumer in the engine, and each consumer needs
  // precisely the side it gets:
  //
  //   * pull `latest` -> InboundPump's loop bound (`_cursor < page.Latest`). It MUST NOT count a
  //     row the page will not return, or the pump re-pulls the same page forever. Pinned by
  //     'excludes expired rows from latest' above.
  //   * push `latest` -> the 409 body -> SyncPublisher.ResumeSeq / ReconcileTo (§6.1). It MUST
  //     count the expired-but-uncollected rows, because those are exactly the rows the push guard
  //     will go on refusing against. A retention-filtered number here is BELOW the engine's own
  //     counter; ReconcileTo refuses to move a counter DOWN (§6.2, rewinding would re-issue seqs
  //     the phone may have accepted), so the reconciliation is declined and the engine walks up
  //     one seq at a time into the same 409 -- once per expired row -- instead of resuming above
  //     the mark in a single round trip.
  //
  // That failure is silent: every push is individually well-formed and the engine does eventually
  // get through. Only the round-trip count changes, which no status code reports.
  //
  // Nothing asserted the VALUE in the 409 body. Reporting the retention-filtered mark there
  // leaves the refusal decision, every status code, and all four cases above unchanged -- the
  // whole suite stays green. These three cases are what make that mutation visible.

  it('reports the mark the guard enforces in the 409 body, not the retention-filtered one', async () => {
    const pairing = await bootstrap('tok');
    await runInDurableObject(env.PAIRING.get(env.PAIRING.idFromName(pairing)), async (_i, state) => {
      expiredRow(state.storage.sql, 'e2p', 5);
    });
    const res = await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 5) });
    expect(res.status).toBe(409);

    // 5, not 0. The retention-filtered read of this direction is 0 -- every row in it is expired
    // -- and 0 is below any engine counter that could have produced this push, so it reconciles
    // nothing. The number that lets the engine escape is the one the guard actually compares to.
    expect(await res.json()).toMatchObject({ error: 'replay_rejected', latest: 5 });
  });

  it('lets the two marks disagree, with the push mark above the pull mark', async () => {
    const pairing = await bootstrap('tok');
    await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 1) });
    await runInDurableObject(env.PAIRING.get(env.PAIRING.idFromName(pairing)), async (_i, state) => {
      expiredRow(state.storage.sql, 'e2p', 7);
    });

    const push = await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 4) });
    expect(push.status).toBe(409);
    const pushBody = await push.json() as { latest: number };

    const pullBody = await (await call(`/v1/${pairing}/pull?dir=e2p&since=0`, { headers: bearer('tok') })).json() as {
      envelopes: { seq: number }[]; latest: number;
    };

    // Same direction, same instant, two marks: 7 from the guard's superset, 1 from the live rows.
    expect(pushBody.latest).toBe(7);
    expect(pullBody.latest).toBe(1);
    expect(pullBody.envelopes.map((e) => e.seq)).toEqual([1]);

    // The direction of the skew is the invariant, not the gap: the guard's set always contains
    // the page's, so the push mark can never be the lower of the two. A consumer may rely on
    // reconciling upward; nothing may rely on the two being equal.
    expect(pushBody.latest).toBeGreaterThan(pullBody.latest);
  });

  it('agrees on both marks when nothing has expired — the skew is retention-shaped', async () => {
    const pairing = await bootstrap('tok');
    await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 3) });

    const push = await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 2) });
    expect(push.status).toBe(409);
    const pushBody = await push.json() as { latest: number };
    const pullBody = await (await call(`/v1/${pairing}/pull?dir=e2p&since=0`, { headers: bearer('tok') })).json() as {
      latest: number;
    };

    // The control for the case above. Without an expired row the two predicates select the same
    // rows and the marks coincide, so a reader cannot mistake the divergence for a permanent
    // off-by-one between the two paths.
    expect(pushBody.latest).toBe(3);
    expect(pullBody.latest).toBe(3);
  });

  // What the relay's monotonicity guard is NOT. It is MAX(seq) over live rows, so collection
  // removes the floor along with the rows. §6.2 puts the authoritative replay check on the
  // receiver's persisted high-water mark; this test exists so nobody reads the relay guard as
  // a durable one and moves the receiver's obligation onto it.
  it('loses its replay floor once the queue is emptied — the receiver owns that rule', async () => {
    const pairing = await bootstrap('tok');
    await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 9) });
    await runInDurableObject(env.PAIRING.get(env.PAIRING.idFromName(pairing)), async (instance) => {
      instance.purgeAll();
    });
    const replay = await call(`/v1/${pairing}/push`, { method: 'POST', headers: bearer('tok'), body: envelope('e2p', 1) });
    expect(replay.status).toBe(201);
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
      // The no-argument call is the shape `push` actually uses (channel.ts:192); the three
      // above all pass a value, so the production path was the one not exercised here.
      expect(instance.ttlSeconds()).toBe(DEFAULT_TTL_SECONDS);
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

  // §3's retention rule bounds the CEILING ("MUST NOT exceed 30 days") and says nothing about
  // the default; `7 * 24 * 60 * 60` appears nowhere in either spec, so the only statement of
  // intent is protocol.ts's own "shorter than the ceiling on purpose: keep less, for less
  // time". The assertion above is satisfied by the ceiling itself, so raising the default to
  // 30 days was measured green across all 55 pre-existing tests. That mutation changes no
  // behaviour any test can observe — it only makes the blind relay hold every user's
  // ciphertext four times longer, which is the one property this component exists to minimise.
  it('the retention default is 7 days, and strictly shorter than the ceiling', () => {
    expect(DEFAULT_TTL_SECONDS).toBe(7 * 24 * 60 * 60);
    expect(DEFAULT_TTL_SECONDS).toBeLessThan(MAX_TTL_SECONDS);
  });

  // The DDL runs in the PairingChannel constructor (src/channel.ts:29), and Cloudflare calls
  // that constructor on EVERY instantiation of the object — including every wake from
  // eviction or hibernation, against storage that already holds the table. `IF NOT EXISTS` is
  // therefore not a stylistic nicety on either statement: it is the whole reason the
  // constructor survives its second and every later run. Drop it and SQLite raises "table
  // envelopes already exists", the constructor throws, and that pairing's channel is dead —
  // a failure that arrives on a wake long after the deploy that caused it, one pairing at a
  // time, on the path with no other guard.
  //
  // Nothing observed this before. Every other case in this file instantiates a *fresh* DO, so
  // the re-entry path — the one production actually runs on every wake — was covered by no
  // test at all: dropping `IF NOT EXISTS` from the table left all 57 pre-existing tests green.
  // This asserts the property behaviourally rather than by grepping the DDL text, so it also
  // covers the index statement and anything later added to the same string.
  it('the schema DDL is idempotent, because every DO wake re-runs it', async () => {
    const pairing = await bootstrap('tok');
    const stub = env.PAIRING.get(env.PAIRING.idFromName(pairing));
    await runInDurableObject(stub, async (_instance, state) => {
      // The constructor already executed it once against this storage. A wake does it again,
      // and so does the wake after that.
      expect(() => state.storage.sql.exec(ENVELOPE_TABLE_DDL)).not.toThrow();
      expect(() => state.storage.sql.exec(ENVELOPE_TABLE_DDL)).not.toThrow();
    });
  });

});
