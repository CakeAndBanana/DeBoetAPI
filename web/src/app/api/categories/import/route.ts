import { getSession } from '@/lib/api';

const API_BASE = process.env.API_BASE_URL ?? 'http://127.0.0.1:8080';

/** An upload, judged against live data; nothing here is cacheable. */
export const dynamic = 'force-dynamic';

/**
 * Passes an edited workbook up to the API, either to be described or to be applied.
 *
 * A Route Handler rather than a Server Action because those cap a request body at 1 MB by
 * default, and a full export is comfortably able to exceed that. The multipart body is relayed
 * untouched — this server has no reason to parse a spreadsheet — with the token attached from
 * the sealed session, which is the only place it exists.
 *
 * `?apply=true` writes. Anything else previews, which is the safe default: a mistyped query
 * string should describe the import, never perform it.
 */
export async function POST(request: Request): Promise<Response> {
  const session = await getSession();

  if (!session.accessToken) {
    return new Response('Not signed in', { status: 401 });
  }

  const apply = new URL(request.url).searchParams.get('apply') === 'true';
  const route = apply ? '/api/v1/categories/import' : '/api/v1/categories/import/preview';

  const upstream = await fetch(`${API_BASE}${route}`, {
    method: 'POST',
    // Content-Type carries the multipart boundary, so it has to travel with the body.
    headers: {
      Authorization: `Bearer ${session.accessToken}`,
      ...(request.headers.get('content-type')
        ? { 'Content-Type': request.headers.get('content-type')! }
        : {}),
    },
    body: request.body,
    // Required by undici whenever a stream is used as a body.
    duplex: 'half',
    signal: request.signal,
    cache: 'no-store',
  } as RequestInit & { duplex: 'half' }).catch(() => null);

  if (!upstream) {
    return new Response(JSON.stringify({ detail: 'Could not reach the API.' }), {
      status: 502,
      headers: { 'Content-Type': 'application/json' },
    });
  }

  // Passed through as-is, status included: a 400 here carries the API's own explanation of what
  // is wrong with the file, which is worth far more than anything this layer could invent.
  return new Response(upstream.body, {
    status: upstream.status,
    headers: {
      'Content-Type': upstream.headers.get('content-type') ?? 'application/json',
      'Cache-Control': 'no-store',
    },
  });
}
