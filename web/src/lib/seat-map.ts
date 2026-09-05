/**
 * The SPA's side of Ordering's live seat map. Read straight from Ordering, not
 * Catalog: the map is per-Seat status, which Catalog does not hold (ADR-0001).
 * Unauthenticated — anyone may look at a seat map, and reading one holds nothing.
 */

/** A seat's computed status, named by the server. */
export type SeatStatus = 'Available' | 'Held' | 'Confirmed'

/** One seat on the map, with the label a buyer reads and its grid position. */
export interface Seat {
  seatId: string
  seatNumber: string
  row: number
  column: string
  status: SeatStatus
}

export interface SeatMap {
  flightId: string
  seats: Seat[]
}

export const SEAT_MAP_PATH = '/api/ordering/seats'

export function seatMapPath(flightId: string): string {
  return `${SEAT_MAP_PATH}?flightId=${encodeURIComponent(flightId)}`
}

/**
 * A flight's live seat map. An empty seat list is a valid answer — a flight
 * Ordering was never seeded seats for — which the page renders as an empty
 * cabin rather than an error.
 */
export async function getSeatMap(flightId: string, signal?: AbortSignal): Promise<SeatMap> {
  const response = await fetch(seatMapPath(flightId), { signal, headers: { accept: 'application/json' } })

  if (!response.ok) {
    throw new Error(`Seat map returned ${response.status}`)
  }

  return (await response.json()) as SeatMap
}
