import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { BOOKINGS_PATH, type Booking } from '@/lib/bookings'
import { CATALOG_PATHS, type Flight } from '@/lib/catalog'
import { type Route } from '@/test/backend'
import { stubPrefersDark } from '@/test/match-media'
import { renderApp } from '@/test/render-app'
import { aSession, signedInGateway, storeSession } from '@/test/session'

/**
 * Booking history through the whole app: a signed-in buyer sees their past
 * Bookings — each with its flight, seats, and price paid — a signed-out visitor
 * is prompted to sign in, and someone with none is told so plainly.
 */
const FLIGHT_ID = '0199f0e2-1111-7000-8000-000000000001'

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

function aBooking(overrides: Partial<Booking> = {}): Booking {
  return {
    bookingId: '0199f0e2-4444-7000-8000-000000000001',
    holdId: '0199f0e2-2222-7000-8000-000000000001',
    userId: aSession().userId,
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

function renderBookings(routes: Record<string, Route>, { signedIn = true } = {}) {
  if (signedIn) {
    storeSession()
  }

  return renderApp(signedInGateway(routes), '/bookings')
}

beforeEach(() => {
  localStorage.clear()
  stubPrefersDark(false)
})

afterEach(() => vi.unstubAllGlobals())

describe('the bookings page', () => {
  it('lists a booking with its flight, seats, and price paid', async () => {
    renderBookings({
      [BOOKINGS_PATH]: async () => Response.json([aBooking()]),
      [CATALOG_PATHS.flight(FLIGHT_ID)]: async () => Response.json(aFlight()),
    })

    expect(await screen.findByText('LHR → BCN')).toBeInTheDocument()
    expect(screen.getByText('Seats 1A, 1B')).toBeInTheDocument()
    expect(screen.getByText('£99.98')).toBeInTheDocument()
  })

  it('tells a signed-in buyer with no bookings that they have none', async () => {
    renderBookings({ [BOOKINGS_PATH]: async () => Response.json([]) })

    expect(await screen.findByText(/haven't booked any flights yet/i)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /browse flights/i })).toBeInTheDocument()
  })

  it('prompts a signed-out visitor to sign in rather than showing an empty list', async () => {
    renderBookings({}, { signedIn: false })

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('shows an error state when the bookings read fails', async () => {
    renderBookings({ [BOOKINGS_PATH]: async () => new Response(null, { status: 500 }) })

    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn't load your bookings/i)
  })

  it('still shows a booking whose flight is no longer in the catalog', async () => {
    renderBookings({
      [BOOKINGS_PATH]: async () => Response.json([aBooking()]),
      [CATALOG_PATHS.flight(FLIGHT_ID)]: async () => new Response(null, { status: 404 }),
    })

    expect(await screen.findByText(/flight no longer listed/i)).toBeInTheDocument()
    expect(screen.getByText('Seats 1A, 1B')).toBeInTheDocument()
  })
})
