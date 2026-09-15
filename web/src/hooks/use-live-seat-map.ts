import { useEffect, useState } from 'react'

import { type Seat, type SeatMap, getSeatMap } from '@/lib/seat-map'
import { type ConnectionFactory, applySeatChanges, subscribeToSeatMap } from '@/lib/seat-map-live'

/** How the hook re-reads the authoritative map on reconnect; the real read by default. */
export type SeatMapResync = (flightId: string) => Promise<SeatMap>

export interface UseLiveSeatMapOptions {
  /** Injects the SignalR connection for tests. */
  createConnection?: ConnectionFactory
  /** Injects the reconnect re-read for tests; defaults to the live seat-map read. */
  resync?: SeatMapResync
}

/**
 * Keeps a seat list live. Seeds from the map read once over HTTP, then holds a
 * per-Flight SignalR subscription open and folds each pushed change into the
 * list, so the grid repaints as other buyers hold, confirm, or let Seats expire
 * — no refresh.
 *
 * On a dropped-then-recovered connection the subscription re-joins the Flight
 * and this hook re-reads Ordering's authoritative map, re-seeding from it: any
 * change missed while disconnected is picked up from true state rather than
 * silently lost. Re-seeds too whenever the initial map changes (a new Flight, or
 * a fresh read), and tears the subscription down and reopens it on a Flight
 * change, so a viewer is only ever subscribed to the Flight on screen.
 */
export function useLiveSeatMap(
  flightId: string,
  initialSeats: Seat[],
  options: UseLiveSeatMapOptions = {},
): Seat[] {
  const { createConnection, resync = getSeatMap } = options
  const [seats, setSeats] = useState(initialSeats)

  // Re-seed from a fresh map read (a new Flight, or a re-fetch) during render
  // rather than in an effect, so the grid never paints a frame of the previous
  // Flight's seats before an effect corrects it. This is React's documented
  // "adjust state while rendering" pattern, tracking the last-seeded map in state.
  const [seededFrom, setSeededFrom] = useState(initialSeats)
  if (seededFrom !== initialSeats) {
    setSeededFrom(initialSeats)
    setSeats(initialSeats)
  }

  useEffect(() => {
    let active = true

    const subscription = subscribeToSeatMap(
      flightId,
      (change) => setSeats((current) => applySeatChanges(current, change)),
      {
        createConnection,
        onReconnected: async () => {
          try {
            const fresh = await resync(flightId)
            // A newer Flight (or unmount) may have superseded this read while it
            // was in flight; don't let it overwrite the current one.
            if (active) {
              setSeats(fresh.seats)
            }
          } catch {
            // Re-sync failed — keep the last-known map; the next reconnect retries.
          }
        },
      },
    )

    return () => {
      active = false
      void subscription.dispose()
    }
  }, [flightId, createConnection, resync])

  return seats
}
