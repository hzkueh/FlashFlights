import { UNREACHABLE, bearer } from '@/lib/auth'
import type { SeatStatus } from '@/lib/seat-map'

/**
 * The SPA's side of Ordering's Hold endpoint. Requesting a Hold is the one write
 * on the browse path, and its whole subtlety is in telling three failures apart:
 * a well-formed request that *lost a race* for Seats someone else holds (retry
 * on other Seats might win), a *malformed* request (retrying the same thing
 * cannot help), and an *unreachable* gateway (nothing to do with the request).
 * Ordering already answers each with its own status; this maps them onto an
 * outcome the checkout branches on, rather than re-deriving them from prose.
 */

export const HOLDS_PATH = '/api/ordering/holds'

/** One Seat carried on a granted Hold, with the label the buyer reads. */
export interface HeldSeat {
  seatId: string
  seatNumber: string
}

/** A granted Hold, in the shape Ordering's `HoldView` serialises. */
export interface Hold {
  holdId: string
  flightId: string
  userId: string
  /** ISO 8601 — the end of the TTL, stamped by Ordering, counted down against locally. */
  expiresAt: string
  pricePerSeat: number
  seats: HeldSeat[]
}

/** A Seat that blocked a Hold, and the status (Held or Confirmed) that blocked it. */
export interface ConflictingSeat {
  seatId: string
  seatNumber: string
  status: SeatStatus
}

/** What the checkout asks for: exactly these Seats, at the price the buyer was shown. */
export interface CreateHoldRequest {
  flightId: string
  seatIds: string[]
  pricePerSeat: number
}

/**
 * The four ways a request to hold can end, kept apart because the buyer is told
 * a different thing for each and only one of them (`conflict`) is worth
 * retrying on other Seats.
 */
export type CreateHoldOutcome =
  | { status: 'granted'; hold: Hold }
  | { status: 'conflict'; seats: ConflictingSeat[] }
  | { status: 'invalid'; message: string }
  | { status: 'error'; message: string }

/** Shown when a well-formed request failed for a reason the SPA cannot name precisely. */
export const HOLD_FAILED = 'Something went wrong holding those seats. Please try again.'

interface ProblemDetails {
  detail?: string
  errors?: Record<string, string[]>
  seats?: ConflictingSeat[]
}

/**
 * Requests a Hold on exactly the given Seats as the token's buyer. Never throws
 * for a refusal — every server answer becomes an outcome — but does re-throw an
 * abort, so a caller cancelling a superseded request is not mistaken for a
 * failure to show.
 */
export async function createHold(
  request: CreateHoldRequest,
  token: string,
  signal?: AbortSignal,
): Promise<CreateHoldOutcome> {
  let response: Response

  try {
    response = await fetch(HOLDS_PATH, {
      method: 'POST',
      headers: { 'content-type': 'application/json', ...bearer(token) },
      body: JSON.stringify(request),
      signal,
    })
  } catch (cause) {
    if (cause instanceof DOMException && cause.name === 'AbortError') {
      throw cause
    }

    return { status: 'error', message: UNREACHABLE }
  }

  if (response.status === 201) {
    return { status: 'granted', hold: (await response.json()) as Hold }
  }

  const problem = (await readJson(response)) as ProblemDetails | null

  if (response.status === 409 && problem?.seats !== undefined) {
    return { status: 'conflict', seats: problem.seats }
  }

  if (response.status === 400) {
    return { status: 'invalid', message: firstError(problem) ?? HOLD_FAILED }
  }

  return { status: 'error', message: problem?.detail ?? HOLD_FAILED }
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    return null
  }
}

/** The first field message from a validation problem — enough to say what was wrong. */
function firstError(problem: ProblemDetails | null): string | undefined {
  const [messages] = Object.values(problem?.errors ?? {})

  return messages?.[0] ?? problem?.detail
}
