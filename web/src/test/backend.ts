import { vi } from 'vitest'

/**
 * The fetch stubs the SPA's two backend-facing suites share. Kept here rather
 * than copied into each so the shape of a health report exists in one place.
 */

export function stubFetch(impl: typeof fetch) {
  vi.stubGlobal('fetch', vi.fn(impl))
}

/** A readiness report in the shape `MapFlashFlightsHealth` writes. */
export const reachable: typeof fetch = async () =>
  Response.json({ service: 'catalog', status: 'Healthy', checks: {} })

/** An unhealthy service: it answered, but cannot serve anything. */
export const unhealthy: typeof fetch = async () =>
  Response.json({ service: 'catalog', status: 'Unhealthy' }, { status: 503 })

/** Nothing at the other end — what the browser throws on a refused connection. */
export const unreachable: typeof fetch = async () => {
  throw new TypeError('Failed to fetch')
}

/** Answers one request. Given the {@link Request} so a stub can assert on what was sent. */
export type Route = (request: Request) => Response | Promise<Response>

/**
 * A fetch stub built from a path-to-answer map, for suites that make more than
 * one kind of call. An unmapped path throws the way an unreachable host does,
 * rather than falling back to something plausible — a missed stub should be a
 * failure that names itself, not a passing test measuring the wrong thing.
 */
export function routed(routes: Record<string, Route>): typeof fetch {
  return async (input, init) => {
    // The SPA calls root-relative paths, which `Request` cannot represent
    // without an origin; the origin itself is never asserted on.
    const request = new Request(new URL(String(input), 'http://gateway.test'), init)
    const route = routes[new URL(request.url).pathname]

    if (route === undefined) {
      throw new TypeError(`Failed to fetch: no route stubbed for ${new URL(request.url).pathname}`)
    }

    return route(request)
  }
}
