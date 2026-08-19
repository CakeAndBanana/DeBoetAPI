import type { SessionOptions } from 'iron-session';

export interface AdminSession {
  accessToken?: string;
  refreshToken?: string;
  /** Epoch millis. Used to refresh slightly early rather than waiting for a 401. */
  accessExpiresAt?: number;
  displayName?: string;
}

export function sessionOptions(): SessionOptions {
  const secret = process.env.SESSION_SECRET;

  if (!secret || secret.length < 32) {
    throw new Error('SESSION_SECRET must be set and at least 32 characters.');
  }

  return {
    password: secret,
    cookieName: 'deboet_admin',
    cookieOptions: {
      httpOnly: true,
      sameSite: 'lax',
      // Secure everywhere except local http development.
      secure: process.env.NODE_ENV === 'production',
      path: '/',
      // Comfortably inside the refresh token's 30 days, so the cookie never outlives the grant.
      maxAge: 60 * 60 * 24 * 14,
    },
  };
}
