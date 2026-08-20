import { getSession } from '@/lib/api';

const API_BASE = process.env.API_BASE_URL ?? 'http://127.0.0.1:8080';

/** Long-lived by nature; nothing here may be cached or statically analysed. */
export const dynamic = 'force-dynamic';

/**
 * Pipes the API's change stream to the browser.
 *
 * Proxied rather than connected to directly because EventSource cannot send an Authorization
 * header, and because the access token must never reach the browser — the same reason every
 * other call goes through this server. The token is read from the sealed session here and
 * attached on the way out.
 *
 * When the token expires the API closes the stream; EventSource then reconnects on its own and
 * this handler runs again, by which point the proxy has refreshed the session. So an expiring
 * token costs a reconnect, not a dead feed.
 */
export async function GET(request: Request): Promise<Response> {
  const session = await getSession();

  if (!session.accessToken) {
    return new Response('Not signed in', { status: 401 });
  }

  const upstream = await fetch(`${API_BASE}/api/v1/events`, {
    headers: { Authorization: `Bearer ${session.accessToken}` },
    // Passing the browser's signal through means closing the tab closes the API connection
    // too, rather than leaving it to time out.
    signal: request.signal,
    cache: 'no-store',
  }).catch(() => null);

  if (!upstream?.ok || !upstream.body) {
    return new Response('Stream unavailable', { status: 503 });
  }

  return new Response(upstream.body, {
    headers: {
      'Content-Type': 'text/event-stream',
      'Cache-Control': 'no-cache, no-transform',
      Connection: 'keep-alive',
      'X-Accel-Buffering': 'no',
    },
  });
}
