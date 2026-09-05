/**
 * The SPA's live half of Ordering's seat map. The initial map is read once from
 * Ordering over HTTP (see `seat-map.ts`); this module keeps it current after
 * that by holding a SignalR connection to Notifications open and applying the
 * per-Seat changes it relays — so a Seat another buyer holds, confirms, or lets
 * expire repaints here without a refresh.
 *
 * A viewer subscribes to the one Flight it is looking at and is pushed that
 * Flight's changes alone. The push is advisory: Ordering's locked grant remains
 * the sole authority on whether a Seat is takeable (ADR-0001), so a change lost
 * or delivered late can only leave a cell briefly stale, never mislead a viewer
 * into a Seat they can't have.
 */
import { type HubConnection, HubConnectionBuilder } from '@microsoft/signalr'

import type { Seat, SeatStatus } from './seat-map'

/** Reached through the gateway, same-origin behind it, like every other SPA call. */
export const SEAT_MAP_HUB_PATH = '/api/notifications/hubs/seat-map'

/** Names shared with `SeatMapHub` on the server — the client/server contract. */
const SEATS_CHANGED = 'SeatsChanged'
const SUBSCRIBE = 'Subscribe'

/** One Seat's new status, as relayed by Notifications. */
export interface SeatChange {
  seatId: string
  status: SeatStatus
}

/** A batch of one Flight's Seat changes — mirrors one committed movement. */
export interface SeatMapChange {
  flightId: string
  seats: SeatChange[]
  /**
   * Ordering's clock when the movements were written. Carried on the wire but
   * not consulted today: over one ordered connection changes arrive in order, so
   * `applySeatChanges` applies each as it comes. Reserved for the reconnect
   * re-sync (ticket 08, item 5) to discard a change that predates a fresh read.
   */
  occurredAt: string
}

/**
 * Applies a batch of changes to a seat list, returning a new list. Matches by
 * seatId and rewrites only the status — the label and grid position came from
 * the initial read and never move. A change naming a Seat not in the list is
 * ignored rather than added: the list is the cabin, and the map only ever
 * repaints Seats already in it.
 */
export function applySeatChanges(seats: Seat[], change: SeatMapChange): Seat[] {
  if (change.seats.length === 0) {
    return seats
  }

  const next = new Map(change.seats.map((seat) => [seat.seatId, seat.status]))

  return seats.map((seat) => {
    const status = next.get(seat.seatId)
    return status === undefined || status === seat.status ? seat : { ...seat, status }
  })
}

/** A live subscription; dispose to leave the Flight's group and close the connection. */
export interface SeatMapSubscription {
  dispose: () => Promise<void>
}

/** Builds the default SignalR connection; swapped for a fake in tests. */
export type ConnectionFactory = (hubPath: string) => HubConnection

const defaultConnectionFactory: ConnectionFactory = (hubPath) =>
  new HubConnectionBuilder().withUrl(hubPath).build()

/**
 * Opens a connection, subscribes to one Flight, and calls <paramref
 * name="onChange"/> for each batch of changes until disposed. Failures to
 * connect are swallowed — live updates are advisory, so a viewer keeps the
 * static map they already read rather than seeing an error. Recovering a dropped
 * connection and re-syncing to true state is a later step (ticket 08, item 5).
 */
export function subscribeToSeatMap(
  flightId: string,
  onChange: (change: SeatMapChange) => void,
  createConnection: ConnectionFactory = defaultConnectionFactory,
): SeatMapSubscription {
  let connection: HubConnection
  try {
    connection = createConnection(SEAT_MAP_HUB_PATH)
  } catch {
    // Couldn't even build a connection (e.g. no browser to host one). Live
    // updates are advisory, so fall back to the static map already on screen
    // rather than letting the seat map fail to render.
    return { dispose: async () => {} }
  }

  connection.on(SEATS_CHANGED, onChange)

  // Chain subscribe onto the start so dispose can await the whole handshake and
  // never race an Unsubscribe ahead of the Subscribe that it undoes.
  const ready = connection
    .start()
    .then(() => connection.invoke(SUBSCRIBE, flightId))
    .catch(() => {})

  return {
    dispose: async () => {
      connection.off(SEATS_CHANGED, onChange)
      await ready
      await connection.stop()
    },
  }
}
