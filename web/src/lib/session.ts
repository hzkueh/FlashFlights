/**
 * What the SPA remembers about being signed in, and the rule for whether it
 * still counts. Kept apart from React and from localStorage so the part a user
 * actually notices — a reload does not sign them out, an expiry does — is
 * testable without rendering or storing anything.
 */

/** Namespaced so it cannot collide with anything else on the gateway's origin. */
export const SESSION_STORAGE_KEY = 'flashflights.session'

/** Exactly what the gateway answers register and login with. */
export interface Session {
  token: string
  /** ISO 8601, as issued — the token's own expiry, not one derived here. */
  expiresAt: string
  userId: string
  email: string
}

function isSession(value: unknown): value is Session {
  const candidate = value as Partial<Session> | null

  return (
    typeof candidate?.token === 'string' &&
    typeof candidate.expiresAt === 'string' &&
    typeof candidate.userId === 'string' &&
    typeof candidate.email === 'string'
  )
}

/**
 * The stored session, or null if there is nothing usable there. Anything that
 * is not a live session — absent, unparseable, the wrong shape, or past its
 * expiry — is treated as absent rather than trusted, because the alternative is
 * a header that says "signed in" over a token every request will be refused for.
 *
 * @param stored Whatever was last persisted, or null on a first visit.
 * @param now Compared against the token's own expiry.
 */
export function resolveStoredSession(stored: string | null, now: Date): Session | null {
  if (stored === null) {
    return null
  }

  let parsed: unknown

  try {
    parsed = JSON.parse(stored)
  } catch {
    return null
  }

  if (!isSession(parsed)) {
    return null
  }

  const expiresAt = Date.parse(parsed.expiresAt)

  return Number.isNaN(expiresAt) || expiresAt <= now.getTime() ? null : parsed
}
