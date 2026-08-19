/**
 * Reads the role claims out of an access token.
 *
 * The signature is deliberately *not* verified here: the API issued this token and is the only
 * thing that enforces authorisation. This is a user-experience gate — refusing a non-admin at
 * the login screen instead of letting every page fail — not a security boundary. Nothing here
 * grants access; it only decides what message to show.
 */
export function rolesFrom(accessToken: string): string[] {
  const payload = accessToken.split('.')[1];
  if (!payload) return [];

  try {
    const json = Buffer.from(payload.replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString();
    const claims = JSON.parse(json) as Record<string, unknown>;
    const role = claims.role;

    if (typeof role === 'string') return [role];
    if (Array.isArray(role)) return role.filter((r): r is string => typeof r === 'string');
    return [];
  } catch {
    return [];
  }
}

export const ADMIN_ROLE = 'Admin';
