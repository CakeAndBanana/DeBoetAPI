import { getIronSession } from 'iron-session';
import { NextResponse, type NextRequest } from 'next/server';
import { applyTokens, isExpiring, refreshTokens } from '@/lib/api';
import { sessionOptions, type AdminSession } from '@/lib/session';

/**
 * Refreshes the access token before the page renders.
 *
 * This lives in middleware rather than in the pages because Next.js only permits writing
 * cookies from middleware, Route Handlers and Server Actions — never during a Server Component
 * render. Refreshing at render time would obtain a rotated refresh token with nowhere to store
 * it, and the next request would replay the consumed one, which the API treats as theft and
 * punishes by revoking the whole chain.
 */
export async function middleware(request: NextRequest) {
  const response = NextResponse.next();
  const session = await getIronSession<AdminSession>(request, response, sessionOptions);

  if (!session.refreshToken) {
    return isLoginRoute(request) ? response : redirectToLogin(request);
  }

  if (isExpiring(session)) {
    const tokens = await refreshTokens(session.refreshToken);

    if (!tokens) {
      // The grant is gone — expired, revoked, or the chain was burned by a replay.
      session.destroy();
      return isLoginRoute(request) ? response : redirectToLogin(request);
    }

    applyTokens(session, tokens);
    await session.save();
  }

  if (isLoginRoute(request)) {
    return NextResponse.redirect(new URL('/categories', request.url));
  }

  return response;
}

function isLoginRoute(request: NextRequest): boolean {
  return request.nextUrl.pathname === '/login';
}

function redirectToLogin(request: NextRequest): NextResponse {
  const url = new URL('/login', request.url);
  if (request.nextUrl.pathname !== '/') {
    url.searchParams.set('next', request.nextUrl.pathname);
  }
  return NextResponse.redirect(url);
}

export const config = {
  // Everything except the auth route handlers, Next internals and static files. The login
  // POST must stay reachable while signed out.
  matcher: ['/((?!api/auth|_next/static|_next/image|favicon.ico).*)'],
};
