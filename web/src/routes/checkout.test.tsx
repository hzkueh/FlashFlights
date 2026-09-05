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

function renderCheckout(routes: Record<string, Route>, { signedIn = true } = {}) {
  if (signedIn) {
    storeSession()
  }

  return renderApp(
    signedInGateway({
      [CATALOG_PATHS.flight(FLIGHT_ID)]: async () => Response.json(aFlight()),
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
