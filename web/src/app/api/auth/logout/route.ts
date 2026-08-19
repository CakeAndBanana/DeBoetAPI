import { NextResponse } from 'next/server';
import { getSession, logout } from '@/lib/api';

export async function POST() {
  const session = await getSession();
  await logout(session.refreshToken);
  session.destroy();

  // A relative Location, deliberately. Building an absolute URL from `request.url` uses the
  // address the server is bound to — 0.0.0.0 in a container — rather than the host the
  // browser asked for, and behind Caddy it would be the internal service address. Browsers
  // resolve a relative Location against the address they actually used, which is correct in
  // every one of those cases.
  return new NextResponse(null, { status: 303, headers: { Location: '/login' } });
}
