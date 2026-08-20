import { getIronSession } from 'iron-session';
import { NextResponse, type NextRequest } from 'next/server';
import { applyTokens, isExpiring, refreshTokens, syncClaims } from '@/lib/api';
import { sessionOptions, type AdminSession } from '@/lib/session';

/**
 * Refreshes the access token before the page renders.
 *
 * (Next 16 renamed this convention from `middleware` to `proxy`.)
 *
 * It lives here rather than in the pages because Next.js only permits writing cookies from
 * the proxy, Route Handlers and Server Actions — never during a Server Component render.
 * Refreshing at render time would obtain a rotated refresh token with nowhere to store it,
 * and the next request would replay the consumed one, which the API treats as theft and
 * punishes by revoking the whole chain.
 */
export async function proxy(request: NextRequest) {
  const response = NextResponse.next();
  const session = await getIronSession<AdminSession>(request, response, sessionOptions());
  const path = request.nextUrl.pathname;
  const onLoginPage = path === '/login';
  const onChangePasswordPage = path === '/change-password';

  if (!session.refreshToken) {
    return onLoginPage ? response : redirectToLogin(request, response);
  }

  if (isExpiring(session)) {
    const tokens = await refreshTokens(session.refreshToken);

    if (!tokens) {
      // The grant is gone — expired, revoked, or the chain was burned by a replay. Destroying
      // writes a clearing cookie onto `response`, which the redirect below carries over;
      // without that the dead cookie would survive and every request would retry the refresh.
      session.destroy();
      return onLoginPage ? response : redirectToLogin(request, response);
    }

    applyTokens(session, tokens);
    await session.save();
  } else if (syncClaims(session)) {
    // A session sealed before roles were stored. Backfilling here means the very next page
    // knows who it is, instead of the header and the nav disagreeing with the API for as
    // long as the access token stays fresh.
    await session.save();
  }

  // An account on an admin-set password can do nothing else until it chooses its own, so
  // every route funnels to the change screen. The API enforces the same rule, so this is
  // signposting rather than the security boundary.
  if (session.mustChangePassword && !onChangePasswordPage) {
    return redirectTo(request, '/change-password', response);
  }

  if (onChangePasswordPage && !session.mustChangePassword) {
    // Reachable voluntarily, but not worth landing on straight after signing in.
    return response;
  }

  if (onLoginPage) {
    return redirectTo(request, '/categories', response);
  }

  return response;
}

/**
 * Redirects to a path on the host the request actually arrived at.
 *
 * `request.url` is not usable: it carries the address the server is bound to — 0.0.0.0 inside
 * a container — so redirects built from it send the browser to a dead address. A relative
 * Location is not usable either, because Next parses this header as an absolute URL and
 * throws ERR_INVALID_URL. `nextUrl` is derived from the incoming request, so cloning it keeps
 * whichever host the browser (or Caddy) asked for.
 *
 * Any Set-Cookie iron-session has already written is carried across, since a redirect is a
 * different response object from the one the session was bound to.
 */
function redirectTo(
  request: NextRequest,
  pathname: string,
  carryCookiesFrom?: NextResponse,
  search?: Record<string, string>,
): NextResponse {
  const url = request.nextUrl.clone();
  url.pathname = pathname;
  url.search = '';
  for (const [key, value] of Object.entries(search ?? {})) {
    url.searchParams.set(key, value);
  }

  const redirect = NextResponse.redirect(url);

  for (const cookie of carryCookiesFrom?.headers.getSetCookie() ?? []) {
    redirect.headers.append('set-cookie', cookie);
  }

  return redirect;
}

function redirectToLogin(request: NextRequest, carryCookiesFrom?: NextResponse): NextResponse {
  const path = request.nextUrl.pathname;

  return redirectTo(request, '/login', carryCookiesFrom, path === '/' ? undefined : { next: path });
}

export const config = {
  // Everything except the auth route handlers, Next internals and static files. The login
  // POST must stay reachable while signed out.
  matcher: ['/((?!api/auth|_next/static|_next/image|favicon.ico).*)'],
};
