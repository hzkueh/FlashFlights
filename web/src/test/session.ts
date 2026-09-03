import { AUTH_PATHS } from '@/lib/auth'
import { BACKEND_READINESS_PATH } from '@/lib/gateway'
import { SESSION_STORAGE_KEY, type Session } from '@/lib/session'
import { type Route, reachable, routed } from '@/test/backend'

/**
 * The signed-in fixtures the auth suites share: a session in the shape the
 * gateway answers with, and the gateway routes a signed-in SPA calls.
 */

export const SIGNED_IN_EMAIL = 'buyer@flashflights.test'

/** Far enough ahead that a slow test run cannot expire it mid-assertion. */
export function aSession(overrides: Partial<Session> = {}): Session {
  return {
    token: 'a-token-the-gateway-signed',
    expiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    userId: '0199f0e2-0000-7000-8000-000000000001',
    email: SIGNED_IN_EMAIL,
    ...overrides,
  }
}

export function storeSession(session: Session = aSession()): Session {
  localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session))

  return session
}

export function storedSession(): Session | null {
  const stored = localStorage.getItem(SESSION_STORAGE_KEY)

  return stored === null ? null : (JSON.parse(stored) as Session)
}

/** The gateway as a signed-in SPA finds it: readiness answers, and the token is still accepted. */
export function signedInGateway(overrides: Record<string, Route> = {}): typeof fetch {
  const session = aSession()

  return routed({
    [BACKEND_READINESS_PATH]: (request) => reachable(request.url),
    [AUTH_PATHS.me]: async () =>
      Response.json({ userId: session.userId, email: session.email }),
    ...overrides,
  })
}

/** A validation problem in the shape `TypedResults.ValidationProblem` writes. */
export function validationProblem(errors: Record<string, string[]>): Response {
  return Response.json({ title: 'One or more validation errors occurred.', errors }, { status: 400 })
}

/** A refusal in the shape `TypedResults.Problem` writes. */
export function refusal(detail: string, status = 401): Response {
  return Response.json({ title: 'Sign in failed', detail }, { status })
}
