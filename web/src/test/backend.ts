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
