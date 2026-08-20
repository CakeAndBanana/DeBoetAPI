import { describe, expect, it } from 'vitest';
import { ADMIN_ROLE, EDITOR_ROLE, mustChangePassword, rolesFrom, subjectFrom } from './claims';

/**
 * Builds a token shaped like the API's, without signing it. Nothing here verifies signatures —
 * these helpers only read a token the server already trusts — so an unsigned one is a faithful
 * stand-in.
 */
function token(payload: Record<string, unknown>): string {
  const encoded = Buffer.from(JSON.stringify(payload))
    .toString('base64')
    .replaceAll('+', '-')
    .replaceAll('/', '_')
    .replace(/=+$/, '');

  return `header.${encoded}.signature`;
}

describe('rolesFrom', () => {
  it('reads a single role, which the API sends as a bare string', () => {
    expect(rolesFrom(token({ role: ADMIN_ROLE }))).toEqual(['Admin']);
  });

  it('reads several roles, which arrive as an array', () => {
    expect(rolesFrom(token({ role: [ADMIN_ROLE, EDITOR_ROLE] }))).toEqual(['Admin', 'Editor']);
  });

  it('returns nothing when the token carries no role at all', () => {
    expect(rolesFrom(token({ sub: 'abc' }))).toEqual([]);
  });

  it('drops non-string entries rather than passing them on', () => {
    expect(rolesFrom(token({ role: ['Admin', 7, null] }))).toEqual(['Admin']);
  });

  it('survives a token that is not a token', () => {
    // A truncated or corrupt cookie must not take the whole page down.
    expect(rolesFrom('')).toEqual([]);
    expect(rolesFrom('nonsense')).toEqual([]);
    expect(rolesFrom('header..signature')).toEqual([]);
    expect(rolesFrom('header.bm90LWpzb24.signature')).toEqual([]);
  });
});

describe('mustChangePassword', () => {
  it('is true only for the string "true", which is how a JWT carries a boolean', () => {
    expect(mustChangePassword(token({ must_change_password: 'true' }))).toBe(true);
  });

  it('is false when the claim is absent, which is the ordinary case', () => {
    expect(mustChangePassword(token({ sub: 'abc' }))).toBe(false);
  });

  it('is false for anything else, rather than trusting a truthy value', () => {
    expect(mustChangePassword(token({ must_change_password: 'false' }))).toBe(false);
    expect(mustChangePassword(token({ must_change_password: true }))).toBe(false);
  });

  it('survives a malformed token', () => {
    expect(mustChangePassword('nonsense')).toBe(false);
  });
});

describe('subjectFrom', () => {
  it('returns the sub claim', () => {
    expect(subjectFrom(token({ sub: '01a0-user' }))).toBe('01a0-user');
  });

  it('is undefined when sub is missing or not a string', () => {
    expect(subjectFrom(token({ role: 'Admin' }))).toBeUndefined();
    expect(subjectFrom(token({ sub: 42 }))).toBeUndefined();
  });

  it('survives a malformed token', () => {
    expect(subjectFrom('nonsense')).toBeUndefined();
  });
});
