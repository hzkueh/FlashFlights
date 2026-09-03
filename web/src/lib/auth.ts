import type { Session } from '@/lib/session'

/**
 * The SPA's side of the gateway's auth endpoints. Root-relative like every
 * other backend call (see `gateway.ts`): the gateway answers these itself
 * rather than proxying them, but that is its business, not the SPA's.
 */
export const AUTH_PATHS = {
  register: '/api/auth/register',
  login: '/api/auth/login',
  me: '/api/auth/me',
} as const

export interface Credentials {
  email: string
  password: string
}

/** Who the gateway says the bearer of a token is. */
export interface SignedInUser {
  userId: string
  email: string
}

/**
 * A refusal, grouped the way the form shows it. The gateway already decides
 * which field a refusal belongs to, so the SPA renders that decision rather
 * than re-deriving it from message text.
 */
export interface AuthProblem {
  email: string[]
  password: string[]
  /** Refusals that belong to the attempt rather than one field. */
  general: string[]
}

/** Shown when the gateway refuses without saying anything renderable. */
export const UNEXPLAINED_FAILURE = 'That did not work. Please try again.'

/** Shown when the gateway could not be reached at all — a different problem from being refused. */
export const UNREACHABLE = 'FlashFlights could not be reached. Check the gateway is running.'

/**
 * Carries the grouped problem rather than a single string, so a form can put
 * each message under the field it belongs to.
 */
export class AuthError extends Error {
  /** The response's status, or 0 when the request never got an answer. */
  readonly status: number

  readonly problem: AuthProblem

  constructor(status: number, problem: AuthProblem) {
    const [first] = [...problem.general, ...problem.email, ...problem.password]

    super(first ?? UNEXPLAINED_FAILURE)
    this.name = 'AuthError'
    this.status = status
    this.problem = problem
  }
}

/** The header every call on behalf of a signed-in User carries. */
export function bearer(token: string): Record<string, string> {
  return { authorization: `Bearer ${token}` }
}

/** Registers a new User and signs them straight in. */
export function register(credentials: Credentials, signal?: AbortSignal): Promise<Session> {
  return postCredentials(AUTH_PATHS.register, credentials, signal)
}

/** Signs a returning User in. */
export function login(credentials: Credentials, signal?: AbortSignal): Promise<Session> {
  return postCredentials(AUTH_PATHS.login, credentials, signal)
}

/**
 * Asks the gateway whether a stored token is still one it accepts. Rejects with
 * a 401 {@link AuthError} when it is not, and with status 0 when the
 * gateway simply could not be reached — which callers must tell apart, because
 * only the first means the User is no longer signed in.
 */
export async function fetchSignedInUser(token: string, signal?: AbortSignal): Promise<SignedInUser> {
  const response = await send(AUTH_PATHS.me, { method: 'GET', headers: bearer(token), signal })

  await throwIfRefused(response)

  return (await response.json()) as SignedInUser
}

async function postCredentials(
  path: string,
  credentials: Credentials,
  signal?: AbortSignal,
): Promise<Session> {
  const response = await send(path, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(credentials),
    signal,
  })

  await throwIfRefused(response)

  return (await response.json()) as Session
}

async function send(path: string, init: RequestInit): Promise<Response> {
  try {
    return await fetch(path, init)
  } catch (cause) {
    // A refused connection and a refused request are different failures, and
    // only one of them means the credentials were wrong.
    if (cause instanceof DOMException && cause.name === 'AbortError') {
      throw cause
    }

    throw new AuthError(0, { email: [], password: [], general: [UNREACHABLE] })
  }
}

async function throwIfRefused(response: Response): Promise<void> {
  if (response.ok) {
    return
  }

  throw new AuthError(response.status, toProblem(await readJson(response)))
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    return null
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

/**
 * Reads ASP.NET's problem-details shapes: a validation problem carries `errors`
 * keyed by field, and everything else carries `detail`. The empty key is
 * ModelState's convention for an error that belongs to no single field.
 */
function toProblem(body: unknown): AuthProblem {
  const { title, detail, errors } = (body ?? {}) as ProblemDetails
  const named = errors ?? {}

  const general = [
    ...(named[''] ?? []),
    ...Object.entries(named)
      .filter(([field]) => field !== '' && field !== 'email' && field !== 'password')
      .flatMap(([, messages]) => messages),
  ]

  if (general.length === 0 && Object.keys(named).length === 0) {
    general.push(detail ?? title ?? UNEXPLAINED_FAILURE)
  }

  return { email: named.email ?? [], password: named.password ?? [], general }
}
