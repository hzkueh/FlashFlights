import { UNREACHABLE, bearer } from '@/lib/auth'
import { type Flight, getFlight } from '@/lib/catalog'
import { HOLDS_PATH } from '@/lib/holds'

/**
 * The SPA's side of Ordering's Booking surface: confirming a Hold into a Booking,
 * and reading back a buyer's past Bookings.
 *
 * Confirm is the second write on the checkout path, and like the Hold write its
 * whole subtlety is in telling the answers apart: a well-formed confirm that came
 * too late because the TTL lapsed (its Seats are likely free again — start over)
 * is a different thing from a Hold that already resolved into a Booking (nothing
 * to retry), from a Hold that is not the caller's or no longer exists, from an
 * unreachable gateway. Ordering already answers each with its own status and a
 * `reason` extension; this maps them onto an outcome the checkout branches on.
 */

export const BOOKINGS_PATH = '/api/ordering/bookings'

/** The confirm endpoint for one Hold, under the same Holds base the create call uses. */
export function confirmHoldPath(holdId: string): string {
  return `${HOLDS_PATH}/${encodeURIComponent(holdId)}/confirm`
}

/** One Seat carried on a Booking, with the label the buyer reads. */
export interface BookedSeat {
  seatId: string
  seatNumber: string
}

/** A Booking, in the shape Ordering's `BookingView` serialises — the same for a fresh confirm and history. */
export interface Booking {
  bookingId: string
  holdId: string
  userId: string
  flightId: string
  /** ISO 8601 — when the simulated payment completed and the Hold became a Booking. */
  confirmedAt: string
  /** The total taken: the FlashPrice frozen onto the Hold times the Seats confirmed. */
  pricePaid: number
  seats: BookedSeat[]
}

/**
 * The four ways a confirm can end, kept apart because the buyer is told a
 * different thing for each: only `expired` invites starting a fresh Hold, and
 * `alreadyConfirmed` means the purchase already went through.
 */
export type ConfirmHoldOutcome =
  | { status: 'confirmed'; booking: Booking }
  | { status: 'expired' }
  | { status: 'alreadyConfirmed' }
  | { status: 'error'; message: string }

/** Shown when a confirm failed for a reason the SPA cannot name precisely. */
export const CONFIRM_FAILED = 'Something went wrong confirming your booking. Please try again.'

/** Shown when the Hold to confirm was gone — expired and swept, or never the caller's. */
export const HOLD_GONE = 'That hold could no longer be found. Its seats may be free again — select seats to try again.'

interface ProblemDetails {
  detail?: string
  reason?: string
}

/**
 * Confirms a live Hold into a Booking as the token's buyer. Never throws for a
 * refusal — every server answer becomes an outcome — but does re-throw an abort,
 * so a caller cancelling a superseded request is not mistaken for a failure.
 */
export async function confirmHold(
  holdId: string,
  token: string,
  signal?: AbortSignal,
): Promise<ConfirmHoldOutcome> {
  let response: Response

  try {
    response = await fetch(confirmHoldPath(holdId), {
      method: 'POST',
      headers: bearer(token),
      signal,
    })
  } catch (cause) {
    if (cause instanceof DOMException && cause.name === 'AbortError') {
      throw cause
    }

    return { status: 'error', message: UNREACHABLE }
  }

  if (response.status === 201) {
    return { status: 'confirmed', booking: (await response.json()) as Booking }
  }

  const problem = (await readJson(response)) as ProblemDetails | null

  // A 409 carries the reason it lost: `expired` invites a fresh Hold, while
  // `alreadyConfirmed` is terminal. The SPA branches on the reason, not the prose.
  if (response.status === 409 && problem?.reason === 'expired') {
    return { status: 'expired' }
  }

  if (response.status === 409 && problem?.reason === 'alreadyConfirmed') {
    return { status: 'alreadyConfirmed' }
  }

  // A Hold that is not the caller's or never existed is one answer (404), the
  // same conflation Ordering makes so Hold ids do not leak. Its Seats are gone
  // from this buyer, so it reads like a lapse: start over.
  if (response.status === 404) {
    return { status: 'error', message: HOLD_GONE }
  }

  return { status: 'error', message: problem?.detail ?? CONFIRM_FAILED }
}

/**
 * The caller's past Bookings, newest first, as Ordering lists them. Throws on a
 * failure the way the catalog reads do — the history page renders loading, ready,
 * and error from that, the same three states every other read has.
 */
export async function listBookings(token: string, signal?: AbortSignal): Promise<Booking[]> {
  const response = await fetch(BOOKINGS_PATH, {
    signal,
    headers: { accept: 'application/json', ...bearer(token) },
  })

  if (!response.ok) {
    throw new Error(`Bookings list returned ${response.status}`)
  }

  return (await response.json()) as Booking[]
}

/** A Booking paired with its Flight's browse metadata — null when Catalog no longer lists that Flight. */
export interface BookingWithFlight {
  booking: Booking
  flight: Flight | null
}

/**
 * Booking history ready to render: the buyer's Bookings from Ordering, each
 * paired with its Flight from Catalog. Two reads from two places, exactly as the
 * detail page does it — Ordering owns the Booking and its Seats, Catalog owns the
 * route and departure the buyer reads. Each Flight is fetched once even when
 * several Bookings share it, and a Flight Catalog no longer lists is a null
 * pairing (a booked Flight that has since left the catalog), not a failure.
 */
export async function loadBookingHistory(
  token: string,
  signal?: AbortSignal,
): Promise<BookingWithFlight[]> {
  const bookings = await listBookings(token, signal)

  const flightIds = [...new Set(bookings.map((booking) => booking.flightId))]
  const flights = await Promise.all(flightIds.map((id) => getFlight(id, signal)))
  const flightById = new Map(flightIds.map((id, index) => [id, flights[index]]))

  return bookings.map((booking) => ({
    booking,
    flight: flightById.get(booking.flightId) ?? null,
  }))
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    return null
  }
}
