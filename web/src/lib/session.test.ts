import { describe, expect, it } from 'vitest'

import { type Session, resolveStoredSession } from '@/lib/session'

const NOW = new Date('2026-09-03T12:00:00Z')

function stored(session: Partial<Session>): string {
  return JSON.stringify({
    token: 'a-token-the-gateway-signed',
    expiresAt: '2026-09-03T13:00:00Z',
    userId: '0199f0e2-0000-7000-8000-000000000001',
    email: 'buyer@flashflights.test',
    ...session,
  })
}

describe('resolving a stored session', () => {
  it('keeps a session whose token has not expired, so a reload does not sign the User out', () => {
    expect(resolveStoredSession(stored({}), NOW)).toMatchObject({ email: 'buyer@flashflights.test' })
  })

  it('has no session on a first visit', () => {
    expect(resolveStoredSession(null, NOW)).toBeNull()
  })

  /** The header would otherwise say "signed in" over a token every request is refused for. */
  it('discards a session whose token has expired', () => {
    expect(resolveStoredSession(stored({ expiresAt: '2026-09-03T11:59:59Z' }), NOW)).toBeNull()
  })

  it('discards a session expiring exactly now rather than racing the next request', () => {
    expect(resolveStoredSession(stored({ expiresAt: NOW.toISOString() }), NOW)).toBeNull()
  })

  it.each([
    ['not json at all', 'signed-in'],
    ['an expiry that is not a date', stored({ expiresAt: 'whenever' })],
    ['no token', JSON.stringify({ expiresAt: '2026-09-03T13:00:00Z', userId: 'u', email: 'e' })],
    ['a token that is not a string', stored({ token: 42 as unknown as string })],
  ])('treats %s as no session rather than trusting it', (_, raw) => {
    expect(resolveStoredSession(raw, NOW)).toBeNull()
  })
})
