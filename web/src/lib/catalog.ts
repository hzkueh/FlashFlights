/**
 * The SPA's side of Catalog's browse endpoints. Root-relative like every other
 * backend call (see `gateway.ts`), and unauthenticated — a visitor browses the
 * catalog without a token.
 */

/** Where a flight's one flash sale is in its window, named by the server. */
export type SaleState = 'Upcoming' | 'Live' | 'Ended'

/**
 * The advisory seat tally shown while browsing (CONTEXT.md's SeatCounts). It
 * trails the movements it is derived from, so it can disagree with a flight's own
 * seat map — never a basis for holding a seat, only a gauge of how full a sale is.
 */
export interface SeatCounts {
  total: number
  available: number
  held: number
  confirmed: number
}

/** One flight as the catalog serves it. `seatCounts` is null until its projection row exists. */
export interface Flight {
  id: string
  flightNumber: string
  origin: string
  destination: string
  departureAt: string
  flashPrice: number
  /**
   * The standing fare this flight is marked down from, or `null` when no saving
   * is advertised (CONTEXT.md's ReferenceFare). Display only — the price paid is
   * always {@link flashPrice}. The saving percentage is derived by
   * `savingsPercent`, never sent by the server.
   */
  referenceFare: number | null
  saleStartsAt: string
  saleEndsAt: string
  saleState: SaleState
  seatCounts: SeatCounts | null
}

export const CATALOG_PATHS = {
  flights: '/api/catalog/flights',
  flight: (flightId: string) => `/api/catalog/flights/${flightId}`,
} as const

/** Every flight running a flash sale, already ordered live-first by the server. */
export async function listFlights(signal?: AbortSignal): Promise<Flight[]> {
  const response = await fetch(CATALOG_PATHS.flights, { signal, headers: { accept: 'application/json' } })

  if (!response.ok) {
    throw new Error(`Catalog list returned ${response.status}`)
  }

  return (await response.json()) as Flight[]
}

/**
 * One flight by id, or null when the catalog has no such flight — a 404 is an
 * answer ("no such flight"), not a failure, so the detail page can tell it apart
 * from a gateway that could not be reached.
 */
export async function getFlight(flightId: string, signal?: AbortSignal): Promise<Flight | null> {
  const response = await fetch(CATALOG_PATHS.flight(flightId), { signal, headers: { accept: 'application/json' } })

  if (response.status === 404) {
    return null
  }

  if (!response.ok) {
    throw new Error(`Catalog flight returned ${response.status}`)
  }

  return (await response.json()) as Flight
}
