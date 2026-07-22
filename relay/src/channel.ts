import { DurableObject } from 'cloudflare:workers';
import {
  DEFAULT_TTL_SECONDS,
  DIRECTIONS,
  ENVELOPE_TABLE_DDL,
  MAX_TTL_SECONDS,
  type Direction,
} from './protocol';

/**
 * One Durable Object per pairing: two queues (engine->phone, phone->engine) of
 * ciphertext blobs, expiring on a TTL.
 *
 * P0 SCAFFOLD. The schema, the TTL policy, and the purge are real, because those are
 * the parts that carry the privacy promise and are worth reviewing now. Enqueue,
 * read, and WebSocket fan-out land in P1 -- the Worker returns 501 for those routes
 * until then, rather than shipping a half-implemented queue that looks finished.
 */
export class PairingChannel extends DurableObject<Env> {
  private readonly sql: SqlStorage;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    this.sql = ctx.storage.sql;
    this.sql.exec(ENVELOPE_TABLE_DDL);
  }

  /**
   * Retention for this pairing. Clamped to the spec ceiling here rather than at the
   * call site, so no caller can widen it -- including a future one that forgets the
   * rule exists.
   */
  ttlSeconds(requested: number = DEFAULT_TTL_SECONDS): number {
    if (!Number.isFinite(requested) || requested <= 0) return DEFAULT_TTL_SECONDS;
    return Math.min(Math.floor(requested), MAX_TTL_SECONDS);
  }

  /** Queue depth per direction. Metadata only -- no envelope content is returned. */
  depth(): Record<Direction, number> {
    const counts = Object.fromEntries(DIRECTIONS.map((d) => [d, 0])) as Record<Direction, number>;
    const rows = this.sql
      .exec<{ dir: string; n: number }>('SELECT dir, COUNT(*) AS n FROM envelopes GROUP BY dir')
      .toArray();
    for (const row of rows) {
      if ((DIRECTIONS as readonly string[]).includes(row.dir)) counts[row.dir as Direction] = row.n;
    }
    return counts;
  }

  /** Delete everything past its expiry. Returns how many rows went. */
  purgeExpired(nowSeconds: number = Math.floor(Date.now() / 1000)): number {
    const before = this.count();
    this.sql.exec('DELETE FROM envelopes WHERE expires_at <= ?', nowSeconds);
    return before - this.count();
  }

  /**
   * Unpair: purge everything for this pairing. Destructive and intentional -- the
   * product promise is that unpairing leaves nothing behind, so this must remove
   * rows rather than mark them.
   */
  purgeAll(): number {
    const removed = this.count();
    this.sql.exec('DELETE FROM envelopes');
    return removed;
  }

  private count(): number {
    return this.sql.exec<{ n: number }>('SELECT COUNT(*) AS n FROM envelopes').one().n;
  }
}
