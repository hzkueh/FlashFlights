import { screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { CATALOG_PATHS, type Flight } from '@/lib/catalog'
import { BACKEND_READINESS_PATH } from '@/lib/gateway'
import { type Route, reachable, routed } from '@/test/backend'
import { stubPrefersDark } from '@/test/match-media'
import { renderApp } from '@/test/render-app'

/**
 * The catalog list through the whole app, signed out — a visitor browses without
 * an account (spec). What a buyer is promised is a scannable list with each
 * flight's route, price, remaining seats, sale state, and time left.
 */
function aFlight(overrides: Partial<Flight> = {}): Flight {
  return {
    id: '0199f0e2-1111-7000-8000-000000000001',
    flightNumber: 'FF412',
    origin: 'LHR',
    destination: 'BCN',
    departureAt: '2026-10-01T09:30:00Z',
    flashPrice: 49.99,
    saleStartsAt: '2026-09-05T11:00:00Z',
    saleEndsAt: '2026-09-05T17:00:00Z',
    saleState: 'Live',
    seatCounts: { total: 12, available: 9, held: 2, confirmed: 1 },
    ...overrides,
  }
}

function browseGateway(routes: Record<string, Route>): typeof fetch {
  return routed({
    [BACKEND_READINESS_PATH]: (request) => reachable(request.url),
    ...routes,
  })
}

beforeEach(() => {
  localStorage.clear()
  stubPrefersDark(false)
})

afterEach(() => vi.unstubAllGlobals())

describe('the flights list', () => {
  it('shows each flight’s route, price, remaining seats and sale state', async () => {
    renderApp(
      browseGateway({ [CATALOG_PATHS.flights]: async () => Response.json([aFlight()]) }),
    )

    const card = await screen.findByRole('link', { name: /LHR/ })
    expect(within(card).getByText('LHR → BCN')).toBeInTheDocument()
    expect(within(card).getByText('£49.99')).toBeInTheDocument()
    expect(within(card).getByText(/9 of 12 seats left/)).toBeInTheDocument()
    expect(within(card).getByText(/on sale now/i)).toBeInTheDocument()
  })

  it('links each flight to its detail page', async () => {
    renderApp(
      browseGateway({ [CATALOG_PATHS.flights]: async () => Response.json([aFlight()]) }),
    )

    const card = await screen.findByRole('link', { name: /LHR/ })
    expect(card).toHaveAttribute('href', '/flights/0199f0e2-1111-7000-8000-000000000001')
  })

  it('renders while signed out, without asking for an account', async () => {
    renderApp(
      browseGateway({ [CATALOG_PATHS.flights]: async () => Response.json([aFlight()]) }),
    )

    expect(await screen.findByRole('link', { name: /LHR/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /sign in/i })).toBeInTheDocument()
  })

  it('shows unknown counts as a dash rather than sold out', async () => {
    renderApp(
      browseGateway({
        [CATALOG_PATHS.flights]: async () => Response.json([aFlight({ seatCounts: null })]),
      }),
    )

    const card = await screen.findByRole('link', { name: /LHR/ })
    expect(within(card).getByText(/Seats — of —/)).toBeInTheDocument()
    expect(within(card).queryByText(/seats left/)).not.toBeInTheDocument()
  })

  it('says so plainly when no sales are scheduled', async () => {
    renderApp(browseGateway({ [CATALOG_PATHS.flights]: async () => Response.json([]) }))

    expect(await screen.findByText(/no flash sales are scheduled/i)).toBeInTheDocument()
  })

  it('surfaces a load failure rather than a blank page', async () => {
    renderApp(
      browseGateway({
        [CATALOG_PATHS.flights]: () => {
          throw new TypeError('Failed to fetch')
        },
      }),
    )

    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn.t load the flights/i)
  })
})
