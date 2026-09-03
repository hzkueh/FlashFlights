/**
 * The SPA's one view of the backend. Every path is root-relative: in the
 * compose stack the gateway serves this SPA and its own API from the same
 * origin, and `npm run dev` reproduces that with a Vite proxy (see
 * `vite.config.ts`), so there is deliberately no base-URL setting to get wrong.
 */

/** The service the connection indicator watches, named wherever it is reported. */
export const WATCHED_SERVICE = 'Catalog'

/**
 * Readiness of one service *through* the gateway, rather than the gateway's
 * own. One call this way covers gateway routing, a real service, its datastore
 * and the bus; the gateway's own readiness covers only the Identity store it
 * happens to host, and would read healthy with all three services down.
 *
 * Readiness, not liveness: liveness is check-free by design, so it answers 200
 * for a service that started before its database and can serve nothing.
 *
 * It watches one service, not all three — so this reports Catalog, not "the
 * backend", and every label around it says so.
 */
export const BACKEND_READINESS_PATH = '/api/catalog/health/ready'

/** The shape `MapFlashFlightsHealth` writes, narrowed to what the SPA shows. */
export interface BackendHealth {
  service: string
  status: string
}

/**
 * Resolves with the watched service's health report, and rejects for every way
 * it can be unreachable — connection refused, a non-2xx status, or a body that
 * is not the report. Callers turn a rejection into "disconnected".
 */
export async function fetchBackendHealth(signal?: AbortSignal): Promise<BackendHealth> {
  const response = await fetch(BACKEND_READINESS_PATH, { signal, headers: { accept: 'application/json' } })

  if (!response.ok) {
    throw new Error(`Backend readiness returned ${response.status}`)
  }

  const report = (await response.json()) as Partial<BackendHealth>

  return { service: report.service ?? WATCHED_SERVICE, status: report.status ?? 'Unknown' }
}
