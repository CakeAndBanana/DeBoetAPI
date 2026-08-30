import { getSession } from '@/lib/api';

const API_BASE = process.env.API_BASE_URL ?? 'http://127.0.0.1:8080';

/** Built fresh on every request; there is nothing here worth caching. */
export const dynamic = 'force-dynamic';

const SPREADSHEET = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

/**
 * Hands the browser the API's spreadsheet of the category tree.
 *
 * Proxied rather than linked to directly, for the same reason the change stream is: the access
 * token is sealed in the session cookie and must never reach the browser, so a plain
 * `<a href="{API_BASE}/…">` could not authenticate. The token is attached here on the way out.
 *
 * The body is passed straight through rather than buffered — this server has no reason to hold
 * a copy of a file it is only relaying.
 */
export async function GET(request: Request): Promise<Response> {
  const session = await getSession();

  if (!session.accessToken) {
    return new Response('Not signed in', { status: 401 });
  }

  const upstream = await fetch(`${API_BASE}/api/v1/categories/export`, {
    headers: { Authorization: `Bearer ${session.accessToken}` },
    // Navigating away or hitting cancel stops the API building the workbook, rather than
    // leaving it to finish for nobody.
    signal: request.signal,
    cache: 'no-store',
  }).catch(() => null);

  if (!upstream?.ok || !upstream.body) {
    // 429 is the one an editor can actually provoke, by asking for the file repeatedly, so it
    // is worth telling them apart from a broken API.
    const status = upstream?.status === 429 ? 429 : 502;
    return new Response(
      status === 429 ? 'Too many exports, try again in a minute' : 'Export unavailable',
      { status },
    );
  }

  return new Response(upstream.body, {
    headers: {
      'Content-Type': upstream.headers.get('content-type') ?? SPREADSHEET,
      // The filename the API chose, so the date in it comes from one place.
      'Content-Disposition':
        upstream.headers.get('content-disposition') ?? 'attachment; filename="categories.xlsx"',
      'Cache-Control': 'no-store',
    },
  });
}
