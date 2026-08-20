import { NextResponse } from 'next/server';
import { apiFetch, getSession, login, applyTokens } from '@/lib/api';
import { ApiError } from '@/lib/problem';

/**
 * Changes the signed-in user's password.
 *
 * A Route Handler rather than a Server Action because the session cookie has to be rewritten
 * afterwards: the API revokes every refresh token on a successful change, so the stored pair
 * is dead the moment it returns. Signing straight back in with the new password replaces it —
 * otherwise the user would be bounced to the login screen immediately after succeeding.
 */
export async function POST(request: Request) {
  const { currentPassword, newPassword } = (await request.json()) as {
    currentPassword?: string;
    newPassword?: string;
  };

  if (!currentPassword || !newPassword) {
    return NextResponse.json({ error: 'Enter your current and new password.' }, { status: 400 });
  }

  const session = await getSession();
  const username = session.displayName;

  try {
    await apiFetch('/api/v1/auth/change-password', {
      method: 'POST',
      body: JSON.stringify({ currentPassword, newPassword }),
    });
  } catch (error) {
    const status = error instanceof ApiError ? error.status : 500;
    const message = error instanceof ApiError ? error.message : 'Could not change the password.';
    return NextResponse.json({ error: message }, { status });
  }

  if (username) {
    const tokens = await login(username, newPassword);
    applyTokens(session, tokens);
    await session.save();
  } else {
    // No username to sign back in with; end the session rather than leave dead tokens sealed
    // in a cookie that looks valid.
    session.destroy();
  }

  return new NextResponse(null, { status: 204 });
}
