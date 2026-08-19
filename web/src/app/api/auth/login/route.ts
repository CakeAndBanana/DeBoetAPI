import { NextResponse } from 'next/server';
import { applyTokens, getSession, login } from '@/lib/api';
import { ApiError } from '@/lib/problem';
import { ADMIN_ROLE, rolesFrom } from '@/lib/claims';

/**
 * Exchanges credentials for tokens and seals them into the session cookie. A Route Handler
 * rather than a Server Action because this is where cookies may be written, and the tokens
 * must never travel to the browser.
 */
export async function POST(request: Request) {
  const { username, password } = (await request.json()) as {
    username?: string;
    password?: string;
  };

  if (!username || !password) {
    return NextResponse.json({ error: 'Enter a username and password.' }, { status: 400 });
  }

  try {
    const tokens = await login(username, password);

    // Refuse non-admins here rather than signing them in and letting every page 403. The
    // API still enforces this on each request; this only produces a clear message.
    if (!rolesFrom(tokens.accessToken).includes(ADMIN_ROLE)) {
      return NextResponse.json(
        { error: 'That account is not an administrator.' },
        { status: 403 },
      );
    }

    const session = await getSession();
    applyTokens(session, tokens);
    session.displayName = username;
    await session.save();

    return new NextResponse(null, { status: 204 });
  } catch (error) {
    const status = error instanceof ApiError ? error.status : 500;
    const message =
      error instanceof ApiError ? error.message : 'Could not reach the API. Is it running?';

    return NextResponse.json({ error: message }, { status });
  }
}
