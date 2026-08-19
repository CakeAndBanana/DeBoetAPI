import { NextResponse } from 'next/server';
import { getSession, logout } from '@/lib/api';

export async function POST(request: Request) {
  const session = await getSession();
  await logout(session.refreshToken);
  session.destroy();

  return NextResponse.redirect(new URL('/login', request.url), { status: 303 });
}
