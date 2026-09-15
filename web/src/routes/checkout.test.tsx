import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { type Booking, confirmHoldPath } from '@/lib/bookings'
import { CATALOG_PATHS, type Flight } from '@/lib/catalog'
import { HOLDS_PATH, type Hold } from '@/lib/holds'
import { SEAT_MAP_PATH, type Seat, type SeatMap } from '@/lib/seat-map'
import { type Route } from '@/test/backend'
import { stubPrefersDark } from '@/test/match-media'
import { renderApp } from '@/test/render-app'
import { signedInGateway, storeSession } from '@/test/session'

/**
 * The checkout flow through the whole app: a signed-in buyer selects Available
 * Seats, requests a Hold on exactly those, is told plainly when they lose the
 * race, and watches a Hold count down to expiry. Confirmation is the next slice
 * (ticket 07's later checkboxes) and is deliberately not exercised here.
 */
const FLIGHT_ID = '0199f0e2-1111-7000-8000-000000000001'
const FLASH_PRICE = 49.99

function aFlight(overrides: Partial<Flight> = {}): Flight {
  return {
    id: FLIGHT_ID,
    flightNumber: 'FF412',
    origin: 'LHR',
    destination: 'BCN',
    departureAt: '2026-10-01T09:30:00Z',
    flashPrice: FLASH_PRICE,
    saleStartsAt: '2026-09-05T11:00:00Z',
    saleEndsAt: '2026-12-05T17:00:00Z',
    saleState: 'Live',
    seatCounts: { total: 2, available: 2, held: 0, confirmed: 0 },
    ...overrides,
  }
}

const SEAT_1A = '0199f0e2-3333-7000-8000-00000000001a'
const SEAT_1B = '0199f0e2-3333-7000-8000-00000000001b'

function seat(overrides: Partial<Seat>): Seat {
  return { seatId: crypto.randomUUID(), seatNumber: '1A', row: 1, column: 'A', status: 'Available', ...overrides }
}

function twoAvailableSeats(): SeatMap {
  return {
    flightId: FLIGHT_ID,
    seats: [
      seat({ seatId: SEAT_1A, seatNumber: '1A', column: 'A' }),
      seat({ seatId: SEAT_1B, seatNumber: '1B', column: 'B' }),
    ],
  }
}

const HOLD_ID = '0199f0e2-2222-7000-8000-000000000001'

function aHold(seats: { seatId: string; seatNumber: string }[], overrides: Partial<Hold> = {}): Hold {
  return {
    holdId: HOLD_ID,
    flightId: FLIGHT_ID,
    userId: '0199f0e2-0000-7000-8000-000000000001',
    expiresAt: new Date(Date.now() + 2 * 60 * 1000).toISOString(),
    pricePerSeat: FLASH_PRICE,
    seats,
    ...overrides,
  }
}

function aBooking(seats: { seatId: string; seatNumber: string }[], overrides: Partial<Booking> = {}): Booking {
  return {
    bookingId: '0199f0e2-4444-7000-8000-000000000001',
    holdId: HOLD_ID,
    userId: '0199f0e2-0000-7000-8000-000000000001',
    flightId: FLIGHT_ID,
    confirmedAt: '2026-09-05T12:00:00Z',
    pricePaid: FLASH_PRICE * seats.length,
    seats,
    ...overrides,
  }
}

const BOTH_SEATS = [
  { seatId: SEAT_1A, seatNumber: '1A' },
  { seatId: SEAT_1B, seatNumber: '1B' },
]

/** Drives the flow up to a granted Hold on 1A and 1B, ready for the confirm step. */
async function holdBothSeats() {
  await userEvent.click(await screen.findByRole('button', { name: 'Seat 1A, Available' }))
  await userEvent.click(screen.getByRole('button', { name: 'Seat 1B, Available' }))
  await userEvent.click(screen.getByRole('button', { name: /hold 2 seats/i }))
  await screen.findByLabelText('Seat 1A, held by you')
}

function renderCheckout(
  routes: Record<string, Route>,
  { signedIn = true, flight = aFlight() } = {},
) {
  if (signedIn) {
    storeSession()
  }

  return renderApp(
    signedInGateway({
      [CATALOG_PATHS.flight(FLIGHT_ID)]: async () => Response.json(flight),
      [SEAT_MAP_PATH]: async () => Response.json(twoAvailableSeats()),
      ...routes,
    }),
    `/flights/${FLIGHT_ID}`,
  )
}

beforeEach(() => {
  localStorage.clear()
  stubPrefersDark(false)
})

afterEach(() => vi.unstubAllGlobals())

describe('the checkout flow', () => {
  it('lets a signed-in buyer select available seats and shows the running total', async () => {
    renderCheckout({})

    await userEvent.click(await screen.findByRole('button', { name: 'Seat 1A, Available' }))

    expect(screen.getByRole('button', { name: 'Seat 1A, Available' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    expect(screen.getByRole('button', { name: /hold 1 seat/i })).toBeInTheDocument()
    // The running total sits in the selection bar alongside the flight header's price.
    expect(screen.getAllByText('£49.99')).toHaveLength(2)
  })

  it('holds exactly the selected seats and starts the countdown', async () => {
    let sent: { seatIds?: string[]; pricePerSeat?: number } = {}
    const holds: Route = async (request) => {
      sent = await request.json()
      return Response.json(
        aHold([
          { seatId: SEAT_1A, seatNumber: '1A' },
          { seatId: SEAT_1B, seatNumber: '1B' },
        ]),
        { status: 201 },
      )
    }
    renderCheckout({ [HOLDS_PATH]: holds })

    await userEvent.click(await screen.findByRole('button', { name: 'Seat 1A, Available' }))
    await userEvent.click(screen.getByRole('button', { name: 'Seat 1B, Available' }))
    await userEvent.click(screen.getByRole('button', { name: /hold 2 seats/i }))

    expect(await screen.findByText(/holding/i)).toHaveTextContent('1A, 1B')
    expect(screen.getByText(/time left/i)).toBeInTheDocument()
    expect(await screen.findByLabelText('Seat 1A, held by you')).toBeInTheDocument()

    // Exactly the seats picked, at the price shown — reserved, not a superset.
    expect(sent.seatIds).toEqual([SEAT_1A, SEAT_1B])
    expect(sent.pricePerSeat).toBe(FLASH_PRICE)
  })

  it('surfaces a lost race, names the taken seats, and keeps the rest holdable', async () => {
    renderCheckout({
      [HOLDS_PATH]: async () =>
        Response.json(
          {
            title: 'Seats no longer available',
            detail: 'These seats are already held or booked: 1A.',
            seats: [{ seatId: SEAT_1A, seatNumber: '1A', status: 'Held' }],
          },
          { status: 409 },
        ),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Seat 1A, Available' }))
    await userEvent.click(screen.getByRole('button', { name: 'Seat 1B, Available' }))
    await userEvent.click(screen.getByRole('button', { name: /hold 2 seats/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/1A.*taken/i)
    // The taken seat now shows as taken (Held) rather than a stale, clickable
    // Available button — the buyer can't re-select the seat they just lost.
    expect(screen.getByLabelText('Seat 1A, Held')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Seat 1A, Available' })).not.toBeInTheDocument()
    // The other seat stays selected and holdable.
    expect(screen.getByRole('button', { name: 'Seat 1B, Available' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    expect(screen.getByRole('button', { name: /hold 1 seat/i })).toBeInTheDocument()
  })

  /**
   * The page gates the hold button on the sale being Live, but it recomputes that
   * from the flight it was served — so a window that closes under an open page,
   * or a sale Ordering has not yet heard open, can still get a post through.
   * Ordering is where the rule is kept (ADR-0003), and its refusal has to land as
   * something other than a lost race: there are no other seats to try, so the
   * selection is dropped rather than left lit under a button that will not work.
   */
  it("tells the buyer the sale is not open when Ordering refuses the hold", async () => {
    renderCheckout({
      [HOLDS_PATH]: async () =>
        Response.json(
          {
            title: 'Flash sale ended',
            detail: "This flight's flash sale has closed, so its seats are no longer on offer at the flash price.",
            reason: 'saleNotOpen',
          },
          { status: 409 },
        ),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Seat 1A, Available' }))
    await userEvent.click(screen.getByRole('button', { name: /hold 1 seat/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/flash sale has closed/i)

    // Nothing stays selected: no other seat would have fared better, so a
    // selection bar offering another hold would only fail again.
    expect(screen.getByRole('button', { name: 'Seat 1A, Available' })).toHaveAttribute(
      'aria-pressed',
      'false',
    )
    expect(screen.queryByRole('button', { name: /hold 1 seat/i })).not.toBeInTheDocument()
  })

  /**
   * The live map's whole problem: a seat repainting silently is indistinguishable
   * from a seat that was always taken. The one that moved says so, and — just as
   * importantly — the ones that did not stay quiet, so the highlight means
   * something.
   */
  it('highlights the seat that just changed under the buyer, and only that one', async () => {
    renderCheckout({
      [HOLDS_PATH]: async () =>
        Response.json(
          {
            title: 'Seats no longer available',
            detail: 'These seats are already held or booked: 1A.',
            seats: [{ seatId: SEAT_1A, seatNumber: '1A', status: 'Held' }],
          },
          { status: 409 },
        ),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Seat 1A, Available' }))
    await userEvent.click(screen.getByRole('button', { name: /hold 1 seat/i }))

    expect(await screen.findByLabelText('Seat 1A, Held')).toHaveAttribute('data-changed', 'true')
    expect(screen.getByRole('button', { name: 'Seat 1B, Available' })).toHaveAttribute(
      'data-changed',
      'false',
    )
  })

  /**
   * The Available-to-Held move is the one the beat exists for, and it is also the
   * one that changes the seat from a button into inert text. If the animated
   * element is rebuilt across that swap, Framer treats it as a mount — which
   * `initial: false` suppresses — and the seat changes with no beat at all. So
   * the cell that animates has to survive the change, whatever renders inside it.
   */
  it('animates the same element across a seat becoming held, rather than rebuilding it', async () => {
    renderCheckout({
      [HOLDS_PATH]: async () =>
        Response.json(
          {
            title: 'Seats no longer available',
            detail: 'These seats are already held or booked: 1A.',
            seats: [{ seatId: SEAT_1A, seatNumber: '1A', status: 'Held' }],
          },
          { status: 409 },
        ),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Seat 1A, Available' }))
    const before = document.querySelector('[data-seat-cell="1A"]')

    await userEvent.click(screen.getByRole('button', { name: /hold 1 seat/i }))
    await screen.findByLabelText('Seat 1A, Held')

    expect(before).not.toBeNull()
    expect(document.querySelector('[data-seat-cell="1A"]')).toBe(before)
  })

  it('releases the seats and tells the buyer when the hold runs out of time', async () => {
    renderCheckout({
      [HOLDS_PATH]: async () =>
        Response.json(
          aHold([{ seatId: SEAT_1A, seatNumber: '1A' }], {
            expiresAt: new Date(Date.now() - 1000).toISOString(),
          }),
          { status: 201 },
        ),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Seat 1A, Available' }))
    await userEvent.click(screen.getByRole('button', { name: /hold 1 seat/i }))

    expect(await screen.findByText(/ran out of time/i)).toBeInTheDocument()
    // Released: the seat is selectable again and nothing is being held.
    expect(await screen.findByRole('button', { name: 'Seat 1A, Available' })).toHaveAttribute(
      'aria-pressed',
      'false',
    )
    expect(screen.queryByText(/holding/i)).not.toBeInTheDocument()
  })

  it('prompts a signed-out visitor to sign in when they try to hold', async () => {
    renderCheckout({}, { signedIn: false })

    await userEvent.click(await screen.findByRole('button', { name: 'Seat 1A, Available' }))
    await userEvent.click(screen.getByRole('button', { name: /sign in to hold seats/i }))

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('confirms the hold and shows a booking confirmation with the seats, flight and price paid', async () => {
    let confirmedHoldId: string | undefined
    renderCheckout({
      [HOLDS_PATH]: async () => Response.json(aHold(BOTH_SEATS), { status: 201 }),
      [confirmHoldPath(HOLD_ID)]: async (request) => {
        confirmedHoldId = new URL(request.url).pathname.split('/').at(-2)
        return Response.json(aBooking(BOTH_SEATS), { status: 201 })
      },
    })

    await holdBothSeats()
    await userEvent.click(screen.getByRole('button', { name: /confirm and pay/i }))

    // The confirmation answers the three things a buyer wants after paying.
    expect(await screen.findByText('Booking confirmed')).toBeInTheDocument()
    expect(screen.getByText('1A, 1B')).toBeInTheDocument()
    expect(screen.getByText('LHR → BCN · FF412')).toBeInTheDocument()
    expect(screen.getByText('£99.98')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /view your bookings/i })).toBeInTheDocument()
    // Confirm hit the exact hold's confirm endpoint.
    expect(confirmedHoldId).toBe(HOLD_ID)
  })

  it('releases the seats and tells the buyer when the hold expired before they paid', async () => {
    renderCheckout({
      [HOLDS_PATH]: async () => Response.json(aHold(BOTH_SEATS), { status: 201 }),
      [confirmHoldPath(HOLD_ID)]: async () =>
        Response.json(
          { title: 'Hold expired', detail: 'This hold reached its time limit.', reason: 'expired' },
          { status: 409 },
        ),
    })

    await holdBothSeats()
    await userEvent.click(screen.getByRole('button', { name: /confirm and pay/i }))

    expect(await screen.findByText(/ran out of time/i)).toBeInTheDocument()
    // Back to a usable selection: the seats are pickable again, nothing is booked.
    expect(await screen.findByRole('button', { name: 'Seat 1A, Available' })).toBeInTheDocument()
    expect(screen.queryByText('Booking confirmed')).not.toBeInTheDocument()
  })

  it('keeps the hold live when a confirm fails, so the buyer can try paying again', async () => {
    renderCheckout({
      [HOLDS_PATH]: async () => Response.json(aHold(BOTH_SEATS), { status: 201 }),
      [confirmHoldPath(HOLD_ID)]: async () => {
        throw new TypeError('Failed to fetch')
      },
    })

    await holdBothSeats()
    await userEvent.click(screen.getByRole('button', { name: /confirm and pay/i }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    // The hold is intact: still counting down, and the pay button is offered again.
    expect(screen.getByText(/time left/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /confirm and pay/i })).toBeEnabled()
  })
})

/**
 * A Hold is a claim on a flash price, so it can only be made while the window
 * offering that price is open. The seat map stays fully readable either side of
 * it — a closed sale is browsable, just not buyable.
 */
describe('a sale outside its window', () => {
  const HOUR = 60 * 60 * 1000

  const ended = aFlight({
    saleState: 'Ended',
    saleStartsAt: new Date(Date.now() - 4 * HOUR).toISOString(),
    saleEndsAt: new Date(Date.now() - HOUR).toISOString(),
  })

  const upcoming = aFlight({
    saleState: 'Upcoming',
    saleStartsAt: new Date(Date.now() + HOUR).toISOString(),
    saleEndsAt: new Date(Date.now() + 4 * HOUR).toISOString(),
  })

  it('shows an ended sale’s seats as read-only, with nothing to press', async () => {
    renderCheckout({}, { flight: ended })

    expect(await screen.findByText(/this flash sale has ended/i)).toBeInTheDocument()
    // Available seats render as inert text once the window shuts, exactly as the
    // signed-out browse map renders them — no button, so nothing to click.
    expect(screen.queryByRole('button', { name: /Seat 1A/ })).not.toBeInTheDocument()
    expect(screen.getByText('A')).toBeInTheDocument()
  })

  it('offers no hold on an ended sale, however the seats are reached', async () => {
    renderCheckout({}, { flight: ended })

    await screen.findByText(/this flash sale has ended/i)
    expect(screen.queryByRole('button', { name: /hold/i })).not.toBeInTheDocument()
    expect(screen.queryByText(/select one or more available seats/i)).not.toBeInTheDocument()
  })

  it('says an upcoming sale is not open yet rather than that it is over', async () => {
    renderCheckout({}, { flight: upcoming })

    expect(await screen.findByText(/hasn.t opened yet/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Seat 1A/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /hold/i })).not.toBeInTheDocument()
  })
})
