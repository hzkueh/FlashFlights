import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  AUTH_PATHS,
  AuthError,
  UNEXPLAINED_FAILURE,
  UNREACHABLE,
  fetchSignedInUser,
  login,
  register,
} from '@/lib/auth'
import { routed, stubFetch, unreachable } from '@/test/backend'
import { aSession, refusal, validationProblem } from '@/test/session'

const CREDENTIALS = { email: 'buyer@flashflights.test', password: 'Flash-Sale-2026' }

afterEach(() => vi.unstubAllGlobals())

describe('registering and signing in', () => {
  it.each([
    ['register', register, AUTH_PATHS.register],
    ['login', login, AUTH_PATHS.login],
  ])('%s posts the credentials and returns the session the gateway issued', async (_, call, path) => {
    const sent: Request[] = []
    stubFetch(
      routed({
        [path]: async (request) => {
          sent.push(request.clone())

          return Response.json(aSession())
        },
      }),
    )

    const session = await call(CREDENTIALS)

    expect(session.email).toBe(CREDENTIALS.email)
    expect(sent[0]!.method).toBe('POST')
    await expect(sent[0]!.json()).resolves.toEqual(CREDENTIALS)
  })

  /** The gateway decides which field a refusal belongs to; the SPA renders that decision. */
  it('keeps a refusal against the field the gateway blamed', async () => {
    stubFetch(
      routed({
        [AUTH_PATHS.register]: async () =>
          validationProblem({ password: ['Passwords must be at least 8 characters.'] }),
      }),
    )

    const error = await register(CREDENTIALS).catch((cause: unknown) => cause)

    expect(error).toBeInstanceOf(AuthError)
    expect((error as AuthError).problem).toEqual({
      email: [],
      password: ['Passwords must be at least 8 characters.'],
      general: [],
    })
  })

  it('reports a refusal that belongs to the attempt rather than a field', async () => {
    stubFetch(
      routed({ [AUTH_PATHS.login]: async () => refusal('That email and password do not match.') }),
    )

    const error = (await login(CREDENTIALS).catch((cause: unknown) => cause)) as AuthError

    expect(error.status).toBe(401)
    expect(error.problem.general).toEqual(['That email and password do not match.'])
  })

  it('still says something when the gateway refuses without explaining', async () => {
    stubFetch(routed({ [AUTH_PATHS.login]: async () => new Response('', { status: 500 }) }))

    const error = (await login(CREDENTIALS).catch((cause: unknown) => cause)) as AuthError

    expect(error.problem.general).toEqual([UNEXPLAINED_FAILURE])
  })

  /**
   * An unreachable gateway is a different failure from a refused sign-in, and
   * the status is how callers tell them apart.
   */
  it('reports an unreachable gateway as status 0 rather than a refusal', async () => {
    stubFetch(unreachable)

    const error = (await login(CREDENTIALS).catch((cause: unknown) => cause)) as AuthError

    expect(error.status).toBe(0)
    expect(error.problem.general).toEqual([UNREACHABLE])
  })
})

describe('asking the gateway who a token belongs to', () => {
  it('sends the token as a bearer credential', async () => {
    const sent: Request[] = []
    stubFetch(
      routed({
        [AUTH_PATHS.me]: async (request) => {
          sent.push(request)

          return Response.json({ userId: 'u', email: CREDENTIALS.email })
        },
      }),
    )

    await fetchSignedInUser('a-token-the-gateway-signed')

    expect(sent[0]!.headers.get('authorization')).toBe('Bearer a-token-the-gateway-signed')
  })

  it('reports a token the gateway no longer accepts as a 401', async () => {
    stubFetch(routed({ [AUTH_PATHS.me]: async () => new Response('', { status: 401 }) }))

    const error = (await fetchSignedInUser('stale').catch((cause: unknown) => cause)) as AuthError

    expect(error.status).toBe(401)
  })
})
