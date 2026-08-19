import { getIronSession } from 'iron-session';
import { NextResponse, type NextRequest } from 'next/server';
import { applyTokens, isExpiring, refreshTokens } from '@/lib/api';
import { sessionOptions, type AdminSession } from '@/lib/session';


export async function proxy(request: NextRequest) {
  const response = NextResponse.next();
  const session = await getIronSession<AdminSession>(request, response, sessionOptions());

  if (!session.refreshToken) {
    return isLoginRoute(request) ? response : redirectToLogin(request);
  }

  if (isExpiring(session)) {
    const tokens = await refreshTokens(session.refreshToken);

    if (!tokens) {
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
  matcher: ['/((?!api/auth|_next/static|_next/image|favicon.ico).*)'],
};
