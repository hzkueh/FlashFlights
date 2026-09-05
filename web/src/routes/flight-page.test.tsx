import { screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { CATALOG_PATHS, type Flight } from '@/lib/catalog'
import { BACKEND_READINESS_PATH } from '@/lib/gateway'
import { SEAT_MAP_PATH, type Seat, type SeatMap } from '@/lib/seat-map'
import { type Route, reachable, routed } from '@/test/backend'
import { stubPrefersDark } from '@/test/match-media'
import { renderApp } from '@/test/render-app'

/**
 * The flight detail page through the whole app, signed out. Its promise: the
 * flight's details from Catalog and the live seat map from Ordering, every seat
 * showing Available, Held or Confirmed — two reads from two places (ADR-0001).
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
    referenceFare: null,
    saleStartsAt: '2026-09-05T11:00:00Z',
    saleEndsAt: '2026-09-05T17:00:00Z',
    saleState: 'Live',
    seatCounts: { total: 3, available: 1, held: 1, confirmed: 1 },
    ...overrides,
  }
}

function seat(overrides: Partial<Seat>): Seat {
  return { seatId: crypto.randomUUID(), seatNumber: '1A', row: 1, column: 'A', status: 'Available', ...overrides }
}

function aSeatMap(seats: Seat[]): SeatMap {
  return { flightId: FLIGHT_ID, seats }
}

function detailGateway(routes: Record<string, Route>): typeof fetch {
  return routed({
    [BACKEND_READINESS_PATH]: (request) => reachable(request.url),
    ...routes,
  })
}

function renderDetail(routes: Record<string, Route>) {
  return renderApp(detailGateway(routes), `/flights/${FLIGHT_ID}`)
}

beforeEach(() => {
  localStorage.clear()
  stubPrefersDark(false)
})

afterEach(() => vi.unstubAllGlobals())

describe('the flight detail page', () => {
  it('shows the flight’s route and price from Catalog', async () => {
    renderDetail({
      [CATALOG_PATHS.flight(FLIGHT_ID)]: async () => Response.json(aFlight()),
      [SEAT_MAP_PATH]: async () => Response.json(aSeatMap([seat({})])),
    })

    expect(await screen.findByRole('heading', { name: 'LHR → BCN' })).toBeInTheDocument()
    expect(screen.getByText('£49.99')).toBeInTheDocument()
  })

  it('renders every seat with its status from Ordering’s live map', async () => {
    renderDetail({
      [CATALOG_PATHS.flight(FLIGHT_ID)]: async () => Response.json(aFlight()),
      [SEAT_MAP_PATH]: async () =>
        Response.json(
          aSeatMap([
            seat({ seatNumber: '1A', column: 'A', status: 'Available' }),
            seat({ seatNumber: '1B', column: 'B', status: 'Held' }),
            seat({ seatNumber: '1C', column: 'C', status: 'Confirmed' }),
          ]),
        ),
    })

    expect(await screen.findByLabelText('Seat 1A, Available')).toBeInTheDocument()
    expect(screen.getByLabelText('Seat 1B, Held')).toBeInTheDocument()
    expect(screen.getByLabelText('Seat 1C, Confirmed')).toBeInTheDocument()
  })

  it('reads the seat map straight from Ordering, not Catalog', async () => {
    const seatMap = vi.fn<Route>(async () => Response.json(aSeatMap([seat({})])))
    renderDetail({
      [CATALOG_PATHS.flight(FLIGHT_ID)]: async () => Response.json(aFlight()),
      [SEAT_MAP_PATH]: seatMap,
    })

    await screen.findByLabelText('Seat 1A, Available')

    const requestedUrl = new URL(seatMap.mock.calls[0][0].url)
    expect(requestedUrl.pathname).toBe('/api/ordering/seats')
    expect(requestedUrl.searchParams.get('flightId')).toBe(FLIGHT_ID)
  })

  it('renders while signed out', async () => {
    renderDetail({
      [CATALOG_PATHS.flight(FLIGHT_ID)]: async () => Response.json(aFlight()),
      [SEAT_MAP_PATH]: async () => Response.json(aSeatMap([seat({})])),
    })

    expect(await screen.findByRole('heading', { name: 'LHR → BCN' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /sign in/i })).toBeInTheDocument()
  })

  it('tells the buyer plainly when the flight is not in the catalog', async () => {
    renderDetail({
      [CATALOG_PATHS.flight(FLIGHT_ID)]: async () => new Response(null, { status: 404 }),
      [SEAT_MAP_PATH]: async () => Response.json(aSeatMap([])),
    })

    expect(await screen.findByRole('heading', { name: /flight not found/i })).toBeInTheDocument()
  })

  it('shows an empty cabin rather than an error when a flight has no seats', async () => {
    renderDetail({
      [CATALOG_PATHS.flight(FLIGHT_ID)]: async () => Response.json(aFlight()),
      [SEAT_MAP_PATH]: async () => Response.json(aSeatMap([])),
    })

    expect(await screen.findByText(/no seats yet/i)).toBeInTheDocument()
  })

  it('surfaces a seat-map failure without losing the flight details', async () => {
    renderDetail({
      [CATALOG_PATHS.flight(FLIGHT_ID)]: async () => Response.json(aFlight()),
      [SEAT_MAP_PATH]: () => {
        throw new TypeError('Failed to fetch')
      },
    })

    expect(await screen.findByRole('heading', { name: 'LHR → BCN' })).toBeInTheDocument()
    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn.t load the seat map/i)
  })
})
