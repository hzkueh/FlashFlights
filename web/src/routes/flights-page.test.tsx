import { act, screen, within } from '@testing-library/react'
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
const HOUR = 60 * 60 * 1000

/**
 * The window is relative to now, not a fixed date: the card computes the sale
 * state from `saleStartsAt`/`saleEndsAt` rather than trusting the `saleState`
 * the server sent, so a fixture whose window had drifted into the past would
 * read as Ended however it labelled itself.
 */
function aFlight(overrides: Partial<Flight> = {}): Flight {
  return {
    id: '0199f0e2-1111-7000-8000-000000000001',
    flightNumber: 'FF412',
    origin: 'LHR',
    destination: 'BCN',
    departureAt: '2026-10-01T09:30:00Z',
    flashPrice: 49.99,
    referenceFare: null,
    saleStartsAt: new Date(Date.now() - HOUR).toISOString(),
    saleEndsAt: new Date(Date.now() + 5 * HOUR).toISOString(),
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

  it('shows the reference fare and saving when a flight is marked down', async () => {
    renderApp(
      browseGateway({
        [CATALOG_PATHS.flights]: async () =>
          Response.json([aFlight({ flashPrice: 149, referenceFare: 229 })]),
      }),
    )

    const card = await screen.findByRole('link', { name: /LHR/ })
    expect(within(card).getByText('£149.00')).toBeInTheDocument()
    expect(within(card).getByText('£229.00')).toBeInTheDocument()
    // (229 - 149) / 229 = 34.9% -> 35
    expect(within(card).getByText(/save 35%/i)).toBeInTheDocument()
  })

  it('shows a bare price, no saving, when a flight has no reference fare', async () => {
    renderApp(
      browseGateway({
        [CATALOG_PATHS.flights]: async () =>
          Response.json([aFlight({ flashPrice: 149, referenceFare: null })]),
      }),
    )

    const card = await screen.findByRole('link', { name: /LHR/ })
    expect(within(card).getByText('£149.00')).toBeInTheDocument()
    expect(within(card).queryByText(/save/i)).not.toBeInTheDocument()
  })

  /**
   * The bug this covers: nothing re-fetches this list, so the state Catalog
   * computed when it answered was rendered forever — a sale opening while the
   * page was on screen left the card reading "Upcoming" behind a countdown sat
   * at 0m 0s. The card recomputes the state from the window instead.
   */
  describe('when a sale opens while the list is on screen', () => {
    beforeEach(() => vi.useFakeTimers({ shouldAdvanceTime: true }))
    afterEach(() => vi.useRealTimers())

    const opensAtTenThirty = aFlight({
      saleState: 'Upcoming',
      saleStartsAt: '2026-09-15T10:30:00Z',
      saleEndsAt: '2026-09-15T18:00:00Z',
    })

    it('flips the card to live without the reader reloading', async () => {
      vi.setSystemTime(new Date('2026-09-15T10:29:58Z'))
      renderApp(
        browseGateway({ [CATALOG_PATHS.flights]: async () => Response.json([opensAtTenThirty]) }),
      )

      const card = await screen.findByRole('link', { name: /LHR/ })
      expect(within(card).getByText(/upcoming/i)).toBeInTheDocument()
      expect(within(card).getByText(/opens in/i)).toBeInTheDocument()

      // The async form, not `advanceTimersByTime`: the sync one moves the clock
      // without flushing the re-render React 19 schedules off the back of it, so
      // the card would still be showing the state it mounted with.
      await act(async () => { await vi.advanceTimersByTimeAsync(3000) })

      const live = screen.getByRole('link', { name: /LHR/ })
      expect(within(live).getByText(/on sale now/i)).toBeInTheDocument()
      expect(within(live).getByText(/ends in/i)).toBeInTheDocument()
      expect(within(live).queryByText(/opens in/i)).not.toBeInTheDocument()
    })

    it('flips a live card to ended when its window closes', async () => {
      vi.setSystemTime(new Date('2026-09-15T17:59:58Z'))
      renderApp(
        browseGateway({
          [CATALOG_PATHS.flights]: async () => Response.json([opensAtTenThirty]),
        }),
      )

      const card = await screen.findByRole('link', { name: /LHR/ })
      expect(within(card).getByText(/on sale now/i)).toBeInTheDocument()

      // The async form, not `advanceTimersByTime`: the sync one moves the clock
      // without flushing the re-render React 19 schedules off the back of it, so
      // the card would still be showing the state it mounted with.
      await act(async () => { await vi.advanceTimersByTimeAsync(3000) })

      // Both the badge and the countdown say it: they are handed the same state,
      // so the card can never read "On sale now" over a dead countdown.
      const ended = screen.getByRole('link', { name: /LHR/ })
      expect(within(ended).getAllByText(/sale ended/i)).toHaveLength(2)
      expect(within(ended).queryByText(/ends in/i)).not.toBeInTheDocument()
    })
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
