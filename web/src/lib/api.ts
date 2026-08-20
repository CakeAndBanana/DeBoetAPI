import { getIronSession } from 'iron-session';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { ADMIN_ROLE, mustChangePassword, rolesFrom, subjectFrom } from './claims';
import { ApiError, messageFor, type ProblemDetails } from './problem';
import { sessionOptions, type AdminSession } from './session';

const API_BASE = process.env.API_BASE_URL ?? 'http://127.0.0.1:8080';


const REFRESH_MARGIN_MS = 60_000;

export interface TokenPair {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

export async function getSession() {
  const session = await getIronSession<AdminSession>(await cookies(), sessionOptions());

  // Backfilled in memory as well as in the proxy, because the proxy's `session.save()` only
  // writes a cookie onto the *response* — this render still reads the one that arrived. Without
  // this the first page load after an upgrade would show an administrator as an editor and turn
  // them away from /users, correcting itself only on the next navigation. Read-only: a Server
  // Component may not write cookies, so the proxy is what makes it stick.
  syncClaims(session);

  return session;
}

const inFlight = new Map<string, Promise<TokenPair | null>>();

export function refreshTokens(refreshToken: string): Promise<TokenPair | null> {
  const existing = inFlight.get(refreshToken);
  if (existing) return existing;

  const pending = (async (): Promise<TokenPair | null> => {
    const response = await fetch(`${API_BASE}/api/v1/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
      cache: 'no-store',
    });

    return response.ok ? ((await response.json()) as TokenPair) : null;
  })();

  inFlight.set(refreshToken, pending);
  pending.finally(() => setTimeout(() => inFlight.delete(refreshToken), 15_000));

  return pending;
}

export function isExpiring(session: AdminSession): boolean {
  if (!session.accessToken || !session.accessExpiresAt) return true;
  return Date.now() >= session.accessExpiresAt - REFRESH_MARGIN_MS;
}

export function applyTokens(session: AdminSession, tokens: TokenPair): void {
  session.accessToken = tokens.accessToken;
  session.refreshToken = tokens.refreshToken;
  session.accessExpiresAt = Date.parse(tokens.accessTokenExpiresAt);

  // Re-read from the fresh token rather than trusting the stored copy: a refresh after the
  // user changes their password is what lifts the diversion, and an admin reset is what
  // reimposes it.
  session.roles = rolesFrom(tokens.accessToken);
  session.mustChangePassword = mustChangePassword(tokens.accessToken);
  session.userId = subjectFrom(tokens.accessToken);
}

/**
 * Re-derives the claim-backed fields from the access token the session already holds.
 *
 * Needed because a session sealed by an older build carries tokens but no `roles`, and
 * `isAdmin` would then quietly report false — an administrator labelled an editor and turned
 * away from their own /users screen until the token happened to expire. The token is right
 * there and says who they are, so read it rather than wait.
 */
export function syncClaims(session: AdminSession): boolean {
  if (!session.accessToken || session.roles !== undefined) return false;

  session.roles = rolesFrom(session.accessToken);
  session.mustChangePassword = mustChangePassword(session.accessToken);
  session.userId = subjectFrom(session.accessToken);
  return true;
}

export function isAdmin(session: AdminSession): boolean {
  return session.roles?.includes(ADMIN_ROLE) ?? false;
}

export async function login(emailOrUsername: string, password: string): Promise<TokenPair> {
  const response = await fetch(`${API_BASE}/api/v1/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: emailOrUsername, password }),
    cache: 'no-store',
  });

  if (!response.ok) {
    const problem = (await response.json().catch(() => undefined)) as ProblemDetails | undefined;
    throw new ApiError(response.status, messageFor(problem, response.status), problem);
  }

  return (await response.json()) as TokenPair;
}

export async function logout(refreshToken: string | undefined): Promise<void> {
  if (!refreshToken) return;

  await fetch(`${API_BASE}/api/v1/auth/logout`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
    cache: 'no-store',
  }).catch(() => undefined); // Signing out locally must succeed even if the API is unreachable.
}

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const session = await getSession();

  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers: {
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...(session.accessToken ? { Authorization: `Bearer ${session.accessToken}` } : {}),
      ...init?.headers,
    },
    cache: 'no-store',
  });

  if (!response.ok) {
    const problem = (await response.json().catch(() => undefined)) as ProblemDetails | undefined;

    if (response.status === 401) redirect('/login');

    throw new ApiError(response.status, messageFor(problem, response.status), problem);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}
