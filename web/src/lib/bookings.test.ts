import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  BOOKINGS_PATH,
  type Booking,
  confirmHold,
  confirmHoldPath,
  listBookings,
  loadBookingHistory,
} from '@/lib/bookings'
import { CATALOG_PATHS, type Flight } from '@/lib/catalog'
import { type Route, routed } from '@/test/backend'
import { stubFetch } from '@/test/backend'

/**
 * The SPA's side of Ordering's Booking surface. `confirmHold` asserts the mapping
 * from each answer a confirm gets onto the outcome the checkout branches on — the
 * load-bearing distinction being that an expired Hold (start over) is a different
 * thing from one already booked (nothing to do), from one gone, from an
 * unreachable gateway. `loadBookingHistory` asserts the two-reads join: Bookings
 * from Ordering, each Flight from Catalog, fetched once per Flight.
 */

const FLIGHT_ID = '0199f0e2-1111-7000-8000-000000000001'
const HOLD_ID = '0199f0e2-2222-7000-8000-000000000001'
const TOKEN = 'a-token-the-gateway-signed'

function aBooking(overrides: Partial<Booking> = {}): Booking {
  return {
    bookingId: '0199f0e2-4444-7000-8000-000000000001',
    holdId: HOLD_ID,
    userId: '0199f0e2-0000-7000-8000-000000000001',
    flightId: FLIGHT_ID,
    confirmedAt: '2026-09-05T12:00:00Z',
    pricePaid: 99.98,
    seats: [
      { seatId: 's-1', seatNumber: '1A' },
      { seatId: 's-2', seatNumber: '1B' },
    ],
    ...overrides,
  }
}

function aFlight(overrides: Partial<Flight> = {}): Flight {
  return {
    id: FLIGHT_ID,
    flightNumber: 'FF412',
    origin: 'LHR',
    destination: 'BCN',
    departureAt: '2026-10-01T09:30:00Z',
    flashPrice: 49.99,
    saleStartsAt: '2026-09-05T11:00:00Z',
    saleEndsAt: '2026-12-05T17:00:00Z',
    saleState: 'Live',
    seatCounts: null,
    ...overrides,
  }
}

afterEach(() => vi.unstubAllGlobals())

describe('confirmHold', () => {
  it('posts to the hold-scoped confirm path as the signed-in buyer, and returns the booking', async () => {
    const booking = aBooking()
    stubFetch(async () => Response.json(booking, { status: 201 }))

    const outcome = await confirmHold(HOLD_ID, TOKEN)

    expect(outcome).toEqual({ status: 'confirmed', booking })
    const [path, init] = vi.mocked(fetch).mock.calls[0]
    expect(path).toBe(confirmHoldPath(HOLD_ID))
    expect(init!.method).toBe('POST')
    expect((init!.headers as Record<string, string>).authorization).toBe(`Bearer ${TOKEN}`)
  })

  it('reports an expired hold as its own outcome, so the buyer is invited to start over', async () => {
    stubFetch(async () =>
      Response.json(
        { title: 'Hold expired', detail: 'This hold reached its time limit.', reason: 'expired' },
        { status: 409 },
      ),
    )

    expect(await confirmHold(HOLD_ID, TOKEN)).toEqual({ status: 'expired' })
  })

  it('reports an already-confirmed hold distinctly from an expired one', async () => {
    stubFetch(async () =>
      Response.json(
        { title: 'Hold already confirmed', detail: 'Already a booking.', reason: 'alreadyConfirmed' },
        { status: 409 },
      ),
    )

    expect(await confirmHold(HOLD_ID, TOKEN)).toEqual({ status: 'alreadyConfirmed' })
  })

  it('reports a hold that is gone (404) as an error the buyer can recover from', async () => {
    stubFetch(async () => new Response(null, { status: 404 }))

    const outcome = await confirmHold(HOLD_ID, TOKEN)

    expect(outcome.status).toBe('error')
  })

  it('reports the gateway being unreachable, distinct from any refusal', async () => {
    stubFetch(async () => {
      throw new TypeError('Failed to fetch')
    })

    expect((await confirmHold(HOLD_ID, TOKEN)).status).toBe('error')
  })

  it('propagates an abort rather than swallowing it as an error', async () => {
    const controller = new AbortController()
    controller.abort()
    stubFetch(async (_input, init) => {
      if (init?.signal?.aborted) throw new DOMException('Aborted', 'AbortError')
      return Response.json(aBooking(), { status: 201 })
    })

    await expect(confirmHold(HOLD_ID, TOKEN, controller.signal)).rejects.toThrow(/abort/i)
  })
})

describe('listBookings', () => {
  it('reads the bookings list as the signed-in buyer', async () => {
    const bookings = [aBooking()]
    stubFetch(async () => Response.json(bookings))

    const result = await listBookings(TOKEN)

    expect(result).toEqual(bookings)
    const [path, init] = vi.mocked(fetch).mock.calls[0]
    expect(path).toBe(BOOKINGS_PATH)
    expect((init!.headers as Record<string, string>).authorization).toBe(`Bearer ${TOKEN}`)
  })

  it('throws when the list read fails, so the page can show its error state', async () => {
    stubFetch(async () => new Response(null, { status: 500 }))

    await expect(listBookings(TOKEN)).rejects.toThrow(/500/)
  })
})

describe('loadBookingHistory', () => {
  it('pairs each booking with its flight from catalog', async () => {
    stubFetch(
      routed({
        [BOOKINGS_PATH]: async () => Response.json([aBooking()]),
        [CATALOG_PATHS.flight(FLIGHT_ID)]: async () => Response.json(aFlight()),
      }),
    )

    const history = await loadBookingHistory(TOKEN)

    expect(history).toHaveLength(1)
    expect(history[0].booking.bookingId).toBe(aBooking().bookingId)
    expect(history[0].flight?.origin).toBe('LHR')
  })

  it('fetches each flight once even when several bookings share it', async () => {
    const catalog = vi.fn<Route>(async () => Response.json(aFlight()))
    stubFetch(
      routed({
        [BOOKINGS_PATH]: async () =>
          Response.json([
            aBooking({ bookingId: 'b-1', holdId: 'h-1' }),
            aBooking({ bookingId: 'b-2', holdId: 'h-2' }),
          ]),
        [CATALOG_PATHS.flight(FLIGHT_ID)]: catalog,
      }),
    )

    const history = await loadBookingHistory(TOKEN)

    expect(history).toHaveLength(2)
    // Two bookings on one flight, but Catalog is asked about the flight only once.
    expect(catalog).toHaveBeenCalledTimes(1)
  })

  it('pairs a booking with null when its flight is no longer in the catalog', async () => {
    stubFetch(
      routed({
        [BOOKINGS_PATH]: async () => Response.json([aBooking()]),
        [CATALOG_PATHS.flight(FLIGHT_ID)]: async () => new Response(null, { status: 404 }),
      }),
    )

    const history = await loadBookingHistory(TOKEN)

    expect(history[0].flight).toBeNull()
  })
})
