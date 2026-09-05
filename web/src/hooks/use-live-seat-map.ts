import { useEffect, useState } from 'react'

import type { Seat } from '@/lib/seat-map'
import { type ConnectionFactory, applySeatChanges, subscribeToSeatMap } from '@/lib/seat-map-live'

/**
 * Keeps a seat list live. Seeds from the map read once over HTTP, then holds a
 * per-Flight SignalR subscription open and folds each pushed change into the
 * list, so the grid repaints as other buyers hold, confirm, or let Seats expire
 * — no refresh.
 *
 * Re-seeds whenever the initial map changes (a new Flight, or a re-read), and
 * tears the subscription down and reopens it on a Flight change, so a viewer is
 * only ever subscribed to the Flight on screen. `createConnection` is injectable
 * for tests; production uses the default SignalR connection.
 */
export function useLiveSeatMap(
  flightId: string,
  initialSeats: Seat[],
  createConnection?: ConnectionFactory,
): Seat[] {
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
    const subscription = subscribeToSeatMap(
      flightId,
      (change) => setSeats((current) => applySeatChanges(current, change)),
      createConnection,
    )

    return () => void subscription.dispose()
  }, [flightId, createConnection])

  return seats
}
