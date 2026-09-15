import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { CATALOG_PATHS, type Flight } from '@/lib/catalog'
import { NOTIFICATION_PATHS } from '@/lib/notifications'
import { SEAT_MAP_PATH } from '@/lib/seat-map'
import { type Route } from '@/test/backend'
import { stubPrefersDark } from '@/test/match-media'
import { renderApp } from '@/test/render-app'
import { signedInGateway, storeSession } from '@/test/session'

/**
 * Watching an upcoming flash sale from the flight page: a signed-in user can ask
 * to be told when it opens and change their mind, a signed-out one is pointed at
 * sign-in, and a sale that is already live or over offers nothing — a Watch
 * fires only on the window opening.
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
    saleStartsAt: '2026-12-05T11:00:00Z',
    saleEndsAt: '2026-12-05T17:00:00Z',
    saleState: 'Upcoming',
    seatCounts: null,
    ...overrides,
  }
}

function renderFlight(
  routes: Record<string, Route> = {},
  { signedIn = true, flight = aFlight() } = {},
) {
  if (signedIn) {
    storeSession()
  }

  return renderApp(
    signedInGateway({
      [CATALOG_PATHS.flight(FLIGHT_ID)]: async () => Response.json(flight),
      [SEAT_MAP_PATH]: async () => Response.json({ flightId: FLIGHT_ID, seats: [] }),
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

describe('watching an upcoming sale', () => {
  it('offers to notify a signed-in user when the sale opens', async () => {
    renderFlight()

    expect(
      await screen.findByRole('button', { name: 'Notify me when this sale opens' }),
    ).toHaveAttribute('aria-pressed', 'false')
  })

  it('asserts the watch and says it is on', async () => {
    const watched: string[] = []
    renderFlight({
      [NOTIFICATION_PATHS.watch(FLIGHT_ID)]: (request) => {
        watched.push(request.method)
        return new Response(null, { status: 204 })
      },
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Notify me when this sale opens' }))

    expect(watched).toEqual(['PUT'])
    expect(await screen.findByRole('button', { name: 'Watching this sale' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  /** The toggle has to come back on for a flight this user already watches. */
  it('shows an existing watch as already on', async () => {
    renderFlight({
      [NOTIFICATION_PATHS.watches]: async () => Response.json({ flightIds: [FLIGHT_ID] }),
    })

    expect(await screen.findByRole('button', { name: 'Watching this sale' })).toBeInTheDocument()
  })

  it('un-watches, so no further alerts are sent for it', async () => {
    const methods: string[] = []
    renderFlight({
      [NOTIFICATION_PATHS.watches]: async () => Response.json({ flightIds: [FLIGHT_ID] }),
      [NOTIFICATION_PATHS.watch(FLIGHT_ID)]: (request) => {
        methods.push(request.method)
        return new Response(null, { status: 204 })
      },
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Watching this sale' }))

    expect(methods).toEqual(['DELETE'])
    expect(
      await screen.findByRole('button', { name: 'Notify me when this sale opens' }),
    ).toBeInTheDocument()
  })

  /**
   * The sale opened while the page sat on screen. The server is right and the
   * page is stale, so the user is told to reload rather than shown a toggle that
   * would keep being refused.
   */
  it('says so when the sale opened before the click landed', async () => {
    renderFlight({
      [NOTIFICATION_PATHS.watch(FLIGHT_ID)]: async () =>
        Response.json({ title: 'Conflict' }, { status: 409 }),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Notify me when this sale opens' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/already started/)
    expect(screen.getByRole('button', { name: 'Notify me when this sale opens' })).toBeInTheDocument()
  })

  it('points a signed-out visitor at sign-in rather than hiding the option', async () => {
    renderFlight({}, { signedIn: false })

    expect(await screen.findByText(/to be told when this sale opens/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Notify me/ })).not.toBeInTheDocument()
  })
})

/**
 * The window moves, not just the label: the page derives the sale state from
 * `saleStartsAt`/`saleEndsAt` rather than trusting `saleState`, so a fixture that
 * called itself Live over a window still in the future would read as Upcoming and
 * offer the watch after all.
 */
describe('a sale that is not upcoming', () => {
  const HOUR = 60 * 60 * 1000

  it('offers no watch on a live sale — there is nothing left to wait for', async () => {
    const live = aFlight({
      saleState: 'Live',
      saleStartsAt: new Date(Date.now() - HOUR).toISOString(),
      saleEndsAt: new Date(Date.now() + HOUR).toISOString(),
    })
    renderFlight({}, { flight: live })

    await screen.findByRole('heading', { name: 'LHR → BCN' })
    expect(screen.queryByRole('button', { name: /Notify me/ })).not.toBeInTheDocument()
  })

  it('offers no watch on an ended sale either', async () => {
    const ended = aFlight({
      saleState: 'Ended',
      saleStartsAt: new Date(Date.now() - 2 * HOUR).toISOString(),
      saleEndsAt: new Date(Date.now() - HOUR).toISOString(),
    })
    renderFlight({}, { flight: ended })

    await screen.findByRole('heading', { name: 'LHR → BCN' })
    expect(screen.queryByRole('button', { name: /Notify me/ })).not.toBeInTheDocument()
  })
})
