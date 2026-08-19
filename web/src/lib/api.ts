import { getIronSession } from 'iron-session';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { ApiError, messageFor, type ProblemDetails } from './problem';
import { sessionOptions, type AdminSession } from './session';

const API_BASE = process.env.API_BASE_URL ?? 'http://127.0.0.1:8080';

/** Refresh this long before the access token actually expires, so requests never race the boundary. */
const REFRESH_MARGIN_MS = 60_000;

export interface TokenPair {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

export async function getSession() {
  return getIronSession<AdminSession>(await cookies(), sessionOptions);
}

/**
 * In-flight refreshes, keyed by the refresh token being consumed.
 *
 * The API treats a replayed refresh token as theft and revokes the user's entire chain, so two
 * concurrent refreshes with the same token would silently sign the admin out. Callers holding
 * the same token therefore share one promise. Entries linger briefly after settling because a
 * request that started before the refresh finished still carries the old cookie, and must be
 * given the new tokens rather than trying to redeem the consumed one itself.
 */
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

/**
 * Calls the API with the session's bearer token.
 *
 * Reads happen during Server Component render, where Next.js forbids writing cookies — so a
 * refresh here could not be persisted, and the rotated token would be lost. Middleware
 * refreshes ahead of render instead; this only reports a 401 so the caller can redirect.
 */
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

    // A 401 here means the grant died between middleware and this call — send the user to
    // sign in rather than rendering an error page they can do nothing with.
    if (response.status === 401) redirect('/login');

    throw new ApiError(response.status, messageFor(problem, response.status), problem);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}
