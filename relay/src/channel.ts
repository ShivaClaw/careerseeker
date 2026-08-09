import { DurableObject } from 'cloudflare:workers';
import {
  DEFAULT_TTL_SECONDS,
  DIRECTIONS,
  ENVELOPE_TABLE_DDL,
  MAX_ENVELOPE_BYTES,
  MAX_TTL_SECONDS,
  PULL_PAGE_SIZE,
  type Direction,
} from './protocol';

/**
 * One Durable Object per pairing: two queues (engine→phone, phone→engine) of ciphertext
 * blobs expiring on a TTL, plus the pairing bootstrap state (§5.2.1–§5.2.3 of
 * docs/Sync-Protocol.md): a token *hash* and at most one pairing-completion message.
 *
 * What this class must never contain: key material, plaintext, or any column that could
 * answer "whose is this?". The storage-inspection test in test/relay.test.ts enforces
 * the schema; the CI grep enforces the absence of a decryption path.
 */
export class PairingChannel extends DurableObject<Env> {
  private readonly sql: SqlStorage;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    this.sql = ctx.storage.sql;
    this.sql.exec(ENVELOPE_TABLE_DDL);
  }

  // ------------------------------------------------------------- auth

  /**
   * The relay stores SHA-256(bearer), never the bearer: a relay storage dump must not
   * yield usable tokens. Comparison is constant-time over the digests.
   */
  private async tokenHash(bearer: string): Promise<Uint8Array> {
    return new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(bearer)));
  }

  private constantTimeEqual(a: Uint8Array, b: Uint8Array): boolean {
    if (a.length !== b.length) return false;
    let diff = 0;
    for (let i = 0; i < a.length; i++) diff |= a[i]! ^ b[i]!;
    return diff === 0;
  }

  private async authorize(bearer: string): Promise<boolean> {
    const stored = await this.ctx.storage.get<Uint8Array>('token_hash');
    if (!stored) return false;
    return this.constantTimeEqual(new Uint8Array(stored), await this.tokenHash(bearer));
  }

  // ------------------------------------------------------------- fetch routing

  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    const segments = url.pathname.split('/').filter(Boolean); // ["v1", pairing, route?]
    const route = segments[2] ?? '';
    const bearer = (request.headers.get('authorization') ?? '').slice('Bearer '.length);

    // Bootstrap is the one route that can run before a token hash exists.
    if (route === 'create' && request.method === 'POST') return this.create(request, bearer);

    if (!(await this.authorize(bearer))) return this.json({ error: 'unauthorized' }, 401);

    switch (`${request.method} ${route}`) {
      case 'POST pair': return this.storeCompletion(request);
      case 'GET pair': return this.takeCompletion();
      case 'POST push': return this.push(request);
      case 'GET pull': return this.pull(url);
      case 'GET live': return this.live(request, url);
      case 'DELETE ': return this.purgeAllAndRespond();
      default: return this.json({ error: 'not_found' }, 404);
    }
  }

  // ------------------------------------------------------------- create / rotate (§5.2.1, §5.2.3)

  private async create(request: Request, bearer: string): Promise<Response> {
    if (!bearer) return this.json({ error: 'unauthorized' }, 401);
    const existing = await this.ctx.storage.get<Uint8Array>('token_hash');

    if (!existing) {
      await this.ctx.storage.put('token_hash', await this.tokenHash(bearer));
      return this.json({ ok: true }, 201);
    }

    // Existing channel: only the current bearer may touch it, and the only thing it may
    // do is rotate the hash (provisional → final). Rotation is one-way and idempotent.
    if (!this.constantTimeEqual(new Uint8Array(existing), await this.tokenHash(bearer))) {
      return this.json({ error: 'unauthorized' }, 401);
    }

    let rotateTo: string | undefined;
    try {
      const body = (await request.json()) as { rotate_to?: string };
      rotateTo = body?.rotate_to;
    } catch {
      // no body → plain re-create attempt on an existing channel
    }

    if (rotateTo === undefined) return this.json({ error: 'exists' }, 409);
    if (!/^[0-9a-f]{64}$/.test(rotateTo)) return this.json({ error: 'bad_request' }, 400);

    const bytes = new Uint8Array(rotateTo.match(/../g)!.map((h) => parseInt(h, 16)));
    await this.ctx.storage.put('token_hash', bytes);
    return this.json({ ok: true, rotated: true }, 200);
  }

  // ------------------------------------------------------------- pairing completion (§5.2.2)

  private async storeCompletion(request: Request): Promise<Response> {
    const raw = await request.text();
    if (raw.length > 16 * 1024) return this.json({ error: 'too_large' }, 413);

    let body: { suite?: string; phone_pub?: string; nonce?: string; ciphertext?: string };
    try { body = JSON.parse(raw); } catch { return this.json({ error: 'bad_request' }, 400); }
    if (!body.suite || !body.phone_pub || !body.nonce || !body.ciphertext) {
      return this.json({ error: 'bad_request' }, 400);
    }

    if (await this.ctx.storage.get('completion')) return this.json({ error: 'exists' }, 409);
    await this.ctx.storage.put('completion', raw);
    return this.json({ ok: true }, 201);
  }

  /** One-shot: the completion is deleted on read, so it cannot be replayed to the engine. */
  private async takeCompletion(): Promise<Response> {
    const stored = await this.ctx.storage.get<string>('completion');
    if (!stored) return this.json({ error: 'not_found' }, 404);
    await this.ctx.storage.delete('completion');
    return new Response(stored, { status: 200, headers: this.headers() });
  }

  // ------------------------------------------------------------- envelopes

  private async push(request: Request): Promise<Response> {
    const raw = await request.text();
    if (raw.length > MAX_ENVELOPE_BYTES + 4096) return this.json({ error: 'too_large' }, 413);

    let env: Record<string, unknown>;
    try { env = JSON.parse(raw); } catch { return this.json({ error: 'bad_request' }, 400); }

    // Header-shape validation only. The relay understands routing metadata and nothing
    // else — it MUST NOT try to interpret the ciphertext, and cannot.
    const dir = env.dir as string;
    const seq = env.seq as number;
    if (
      env.v !== 1
      || !DIRECTIONS.includes(dir as Direction)
      || !Number.isInteger(seq) || seq < 1
      || typeof env.ts !== 'string'
      || typeof env.key_id !== 'string'
      || typeof env.nonce !== 'string'
      || typeof env.ciphertext !== 'string'
      || (env.sig !== undefined && typeof env.sig !== 'string')
    ) {
      return this.json({ error: 'bad_request' }, 400);
    }
    if ((env.ciphertext as string).length > MAX_ENVELOPE_BYTES) return this.json({ error: 'too_large' }, 413);

    // Relay-side monotonicity: duplicates and regressions are refused at the door, so a
    // compromised sender cannot fill the queue with replays for the receiver to reject.
    const last = this.sql
      .exec<{ m: number | null }>('SELECT MAX(seq) AS m FROM envelopes WHERE dir = ?', dir)
      .one().m;
    if (last !== null && seq <= last) return this.json({ error: 'replay_rejected', latest: last }, 409);

    const ttl = this.ttlSeconds();
    const expires = Math.floor(Date.now() / 1000) + ttl;
    this.sql.exec(
      'INSERT INTO envelopes (dir, seq, ts, key_id, nonce, ciphertext, size, expires_at) VALUES (?,?,?,?,?,?,?,?)',
      dir, seq, env.ts, env.key_id, env.nonce, raw, raw.length, expires,
    );

    // Earliest-expiry alarm drives the TTL purge; hibernation-safe.
    const current = await this.ctx.storage.getAlarm();
    const due = expires * 1000;
    if (current === null || due < current) await this.ctx.storage.setAlarm(due);

    // Live fan-out: sockets are tagged with the direction they LISTEN to.
    for (const ws of this.ctx.getWebSockets(dir)) {
      try { ws.send(raw); } catch { /* socket already closing; pull will catch it up */ }
    }

    return this.json({ ok: true, seq }, 201);
  }

  private pull(url: URL): Response {
    const dir = url.searchParams.get('dir') ?? '';
    if (!DIRECTIONS.includes(dir as Direction)) return this.json({ error: 'bad_request' }, 400);
    const since = Number(url.searchParams.get('since') ?? '0');
    if (!Number.isInteger(since) || since < 0) return this.json({ error: 'bad_request' }, 400);

    const rows = this.sql
      .exec<{ ciphertext: string; seq: number }>(
        'SELECT ciphertext, seq FROM envelopes WHERE dir = ? AND seq > ? ORDER BY seq LIMIT ?',
        dir, since, PULL_PAGE_SIZE,
      )
      .toArray();

    const latest = this.sql
      .exec<{ m: number | null }>('SELECT MAX(seq) AS m FROM envelopes WHERE dir = ?', dir)
      .one().m ?? 0;

    return new Response(
      `{"envelopes":[${rows.map((r) => r.ciphertext).join(',')}],"latest":${latest}}`,
      { status: 200, headers: this.headers() },
    );
  }

  private live(request: Request, url: URL): Response {
    if (request.headers.get('upgrade')?.toLowerCase() !== 'websocket') {
      return this.json({ error: 'upgrade_required' }, 426);
    }
    const dir = url.searchParams.get('dir') ?? '';
    if (!DIRECTIONS.includes(dir as Direction)) return this.json({ error: 'bad_request' }, 400);

    const pair = new WebSocketPair();
    // Hibernation API: the socket survives DO eviction without billing for idle time.
    this.ctx.acceptWebSocket(pair[1], [dir]);
    return new Response(null, { status: 101, webSocket: pair[0] });
  }

  async webSocketMessage(ws: WebSocket): Promise<void> {
    // The live channel is one-way (server → client). Anything a client sends is a
    // protocol error; close rather than interpret.
    ws.close(1003, 'live channel is receive-only');
  }

  async webSocketClose(): Promise<void> { /* nothing to clean up; tags die with the socket */ }

  // ------------------------------------------------------------- TTL purge

  async alarm(): Promise<void> {
    this.purgeExpired();
    const next = this.sql
      .exec<{ m: number | null }>('SELECT MIN(expires_at) AS m FROM envelopes')
      .one().m;
    if (next !== null) await this.ctx.storage.setAlarm(next * 1000);
  }

  // ------------------------------------------------------------- helpers (also used by tests)

  ttlSeconds(requested: number = DEFAULT_TTL_SECONDS): number {
    if (!Number.isFinite(requested) || requested <= 0) return DEFAULT_TTL_SECONDS;
    return Math.min(Math.floor(requested), MAX_TTL_SECONDS);
  }

  depth(): Record<Direction, number> {
    const counts = Object.fromEntries(DIRECTIONS.map((d) => [d, 0])) as Record<Direction, number>;
    for (const row of this.sql.exec<{ dir: string; n: number }>('SELECT dir, COUNT(*) AS n FROM envelopes GROUP BY dir').toArray()) {
      if ((DIRECTIONS as readonly string[]).includes(row.dir)) counts[row.dir as Direction] = row.n;
    }
    return counts;
  }

  purgeExpired(nowSeconds: number = Math.floor(Date.now() / 1000)): number {
    const before = this.count();
    this.sql.exec('DELETE FROM envelopes WHERE expires_at <= ?', nowSeconds);
    return before - this.count();
  }

  purgeAll(): number {
    const removed = this.count();
    this.sql.exec('DELETE FROM envelopes');
    return removed;
  }

  private async purgeAllAndRespond(): Promise<Response> {
    const removed = this.purgeAll();
    await this.ctx.storage.deleteAll();
    for (const ws of this.ctx.getWebSockets()) {
      try { ws.close(1001, 'unpaired'); } catch { /* already closed */ }
    }
    return this.json({ ok: true, purged: removed }, 200);
  }

  private count(): number {
    return this.sql.exec<{ n: number }>('SELECT COUNT(*) AS n FROM envelopes').one().n;
  }

  private headers(): HeadersInit {
    return {
      'content-type': 'application/json; charset=utf-8',
      'cache-control': 'no-store',
      'referrer-policy': 'no-referrer',
      'x-content-type-options': 'nosniff',
    };
  }

  private json(body: unknown, status: number): Response {
    return new Response(JSON.stringify(body), { status, headers: this.headers() });
  }
}
