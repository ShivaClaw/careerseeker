import { isValidPairingId, PROTOCOL_VERSION } from './protocol';

export { PairingChannel } from './channel';

/**
 * The CareerSeeker blind relay.
 *
 * It moves ciphertext between one engine and one paired phone. It holds no keys, no
 * accounts, and no plaintext, and it must never learn what it is carrying. See
 * docs/Sync-Protocol.md sections 1 and 2.
 *
 * P0 SCAFFOLD: routing, auth shape, and rejection behaviour are real. The queue
 * operations return 501 until P1. That ordering is deliberate -- the parts that carry
 * the privacy promise (what gets stored, how long, what is refused) are worth
 * reviewing before the parts that move bytes.
 */

const NOT_IMPLEMENTED_IN_P0 = 'Not implemented in P0. Queue operations land in P1.';

function json(body: unknown, status: number, extra?: HeadersInit): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      'content-type': 'application/json; charset=utf-8',
      'cache-control': 'no-store',
      'referrer-policy': 'no-referrer',
      'x-content-type-options': 'nosniff',
      ...extra,
    },
  });
}

function error(code: string, status: number, detail?: string): Response {
  // `detail` is for operators and must never carry envelope content -- there is no
  // path by which it could here, since nothing decrypts, but the rule is stated at
  // the point where a future change would be tempted to break it.
  return json({ error: code, detail }, status);
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    const segments = url.pathname.split('/').filter(Boolean);

    // /v1/health -- liveness only. Deliberately reveals nothing about any pairing,
    // and requires no credential, so uptime checks need no secret.
    if (segments.length === 2 && segments[0] === 'v1' && segments[1] === 'health') {
      if (request.method !== 'GET') return error('method_not_allowed', 405);
      return json({ ok: true, protocol: PROTOCOL_VERSION, phase: 'p0-scaffold' }, 200);
    }

    if (segments[0] !== 'v1' || segments.length < 2) return error('not_found', 404);

    const pairing = segments[1];
    if (!pairing || !isValidPairingId(pairing)) return error('pairing_unknown', 404);

    // Authorization is checked BEFORE dispatching, including for routes that are not
    // implemented yet. Otherwise an unauthenticated caller could map which routes
    // exist by reading 501-vs-401, and the P1 implementation would inherit an auth
    // gap that no test covers.
    const auth = request.headers.get('authorization');
    if (!auth?.startsWith('Bearer ') || auth.length <= 'Bearer '.length) {
      return error('unauthorized', 401);
    }

    const route = segments[2];

    if (segments.length === 2 && request.method === 'DELETE') {
      return error('not_implemented', 501, NOT_IMPLEMENTED_IN_P0);
    }

    switch (route) {
      case 'push':
        if (request.method !== 'POST') return error('method_not_allowed', 405);
        return error('not_implemented', 501, NOT_IMPLEMENTED_IN_P0);

      case 'pull':
        if (request.method !== 'GET') return error('method_not_allowed', 405);
        return error('not_implemented', 501, NOT_IMPLEMENTED_IN_P0);

      case 'live':
        if (request.headers.get('upgrade')?.toLowerCase() !== 'websocket') {
          return error('upgrade_required', 426);
        }
        return error('not_implemented', 501, NOT_IMPLEMENTED_IN_P0);

      default:
        return error('not_found', 404);
    }
  },
} satisfies ExportedHandler<Env>;
