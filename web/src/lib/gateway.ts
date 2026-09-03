/**
 * The SPA's one view of the backend. Every path is root-relative: in the
 * compose stack the gateway serves this SPA and its own API from the same
 * origin, and `npm run dev` reproduces that with a Vite proxy (see
 * `vite.config.ts`), so there is deliberately no base-URL setting to get wrong.
 */

/**
 * Readiness of a service *through* the gateway, rather than the gateway's own.
 * Two reasons: the gateway also serves this SPA, so a gateway that cannot
 * answer cannot deliver the page asking either — the indicator would never
 * legitimately read disconnected. And readiness, not liveness: liveness is
 * check-free by design (a service that started before its database still
 * answers 200), so it would report connected for a service that can serve
 * nothing. Catalog is the service this SPA reaches for first.
 */
export const BACKEND_READINESS_PATH = '/api/catalog/health/ready'

/** The shape `MapFlashFlightsHealth` writes, narrowed to what the SPA shows. */
export interface BackendHealth {
  service: string
  status: string
}

/**
 * Resolves with the backend's health report, and rejects for every way it can
 * be unreachable — connection refused, a non-2xx status, or a body that is not
 * the report. Callers turn a rejection into "disconnected".
 */
export async function fetchBackendHealth(signal?: AbortSignal): Promise<BackendHealth> {
  const response = await fetch(BACKEND_READINESS_PATH, { signal, headers: { accept: 'application/json' } })

  if (!response.ok) {
    throw new Error(`Backend readiness returned ${response.status}`)
  }

  const report = (await response.json()) as Partial<BackendHealth>

  return { service: report.service ?? 'backend', status: report.status ?? 'Unknown' }
}
