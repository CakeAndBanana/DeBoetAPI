import { NextResponse } from 'next/server';
import { applyTokens, getSession, login } from '@/lib/api';
import { ApiError } from '@/lib/problem';
import { ADMIN_ROLE, EDITOR_ROLE, mustChangePassword, rolesFrom } from '@/lib/claims';


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

    const roles = rolesFrom(tokens.accessToken);
    if (!roles.includes(ADMIN_ROLE) && !roles.includes(EDITOR_ROLE)) {
      return NextResponse.json(
        { error: 'That account has no access to the admin panel.' },
        { status: 403 },
      );
    }

    const session = await getSession();
    applyTokens(session, tokens);
    session.displayName = username;
    session.roles = roles;
    session.mustChangePassword = mustChangePassword(tokens.accessToken);
    await session.save();

    return new NextResponse(null, { status: 204 });
  } catch (error) {
    const status = error instanceof ApiError ? error.status : 500;
    const message =
      error instanceof ApiError ? error.message : 'Could not reach the API. Is it running?';

    return NextResponse.json({ error: message }, { status });
  }
}
