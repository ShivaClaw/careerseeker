import { isValidPairingId, PROTOCOL_VERSION } from './protocol';

export { PairingChannel } from './channel';

/**
 * The CareerSeeker blind relay.
 *
 * It moves ciphertext between one engine and one paired phone. It holds no keys, no
 * accounts, and no plaintext, and it must never learn what it is carrying. See
 * docs/Sync-Protocol.md sections 1 and 2.
 *
 * The Worker validates shape (pairing id, bearer presence, method) and forwards to the
 * pairing's Durable Object, which owns the token hash and does the real authorization.
 * Bearer *presence* is still checked here, before dispatch: an unauthenticated caller
 * learns nothing about which routes exist.
 */

function json(body: unknown, status: number): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      'content-type': 'application/json; charset=utf-8',
      'cache-control': 'no-store',
      'referrer-policy': 'no-referrer',
      'x-content-type-options': 'nosniff',
    },
  });
}

const ROUTES: Record<string, string[]> = {
  create: ['POST'],
  pair: ['POST', 'GET'],
  push: ['POST'],
  pull: ['GET'],
  live: ['GET'],
  '': ['DELETE'],
};

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    const segments = url.pathname.split('/').filter(Boolean);

    // /v1/health -- liveness only. Reveals nothing about any pairing; needs no secret.
    if (segments.length === 2 && segments[0] === 'v1' && segments[1] === 'health') {
      if (request.method !== 'GET') return json({ error: 'method_not_allowed' }, 405);
      return json({ ok: true, protocol: PROTOCOL_VERSION, phase: 'p1' }, 200);
    }

    if (segments[0] !== 'v1' || segments.length < 2 || segments.length > 3) {
      return json({ error: 'not_found' }, 404);
    }

    const pairing = segments[1];
    if (!pairing || !isValidPairingId(pairing)) return json({ error: 'pairing_unknown' }, 404);

    // Presence check before dispatch: 401 always beats 404/405, so routes cannot be
    // enumerated without a credential. The DO does the real constant-time comparison.
    const auth = request.headers.get('authorization');
    if (!auth?.startsWith('Bearer ') || auth.length <= 'Bearer '.length) {
      return json({ error: 'unauthorized' }, 401);
    }

    const route = segments[2] ?? '';
    const allowed = ROUTES[route];
    if (allowed === undefined) return json({ error: 'not_found' }, 404);
    if (!allowed.includes(request.method)) return json({ error: 'method_not_allowed' }, 405);
    if (route === 'live' && request.headers.get('upgrade')?.toLowerCase() !== 'websocket') {
      return json({ error: 'upgrade_required' }, 426);
    }

    const stub = env.PAIRING.get(env.PAIRING.idFromName(pairing));
    return stub.fetch(request);
  },
} satisfies ExportedHandler<Env>;
