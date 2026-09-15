import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { Countdown, SaleCountdown } from '@/components/countdown'
import type { Flight } from '@/lib/catalog'
import { stubMediaPreferences } from '@/test/match-media'

/**
 * The one countdown treatment both timers in the app wear. What is asserted here
 * is what a buyer can act on: the urgency the element declares, and whether it is
 * moving — colour is a token, not a behaviour, so it is left to the theme.
 */
beforeEach(() => stubMediaPreferences())
afterEach(() => vi.unstubAllGlobals())

function aFlight(overrides: Partial<Flight> = {}): Flight {
  return {
    id: 'flight-1',
    flightNumber: 'FF412',
    origin: 'LHR',
    destination: 'BCN',
    departureAt: '2026-10-01T09:00:00Z',
    flashPrice: 149,
    referenceFare: 229,
    saleStartsAt: '2026-09-15T10:00:00Z',
    saleEndsAt: '2026-09-15T18:00:00Z',
    saleState: 'Live',
    seatCounts: null,
    ...overrides,
  }
}

describe('Countdown', () => {
  it('declares its urgency, so the treatment and the number always agree', () => {
    render(<Countdown urgency="soon">1m 0s</Countdown>)

    expect(screen.getByText('1m 0s')).toHaveAttribute('data-urgency', 'soon')
  })

  it('pulses once a countdown turns critical', () => {
    render(<Countdown urgency="critical">0m 8s</Countdown>)

    expect(screen.getByText('0m 8s')).toHaveAttribute('data-pulse', 'on')
  })

  it('sits still while there is still time', () => {
    render(<Countdown urgency="calm">1h 40m</Countdown>)

    expect(screen.getByText('1h 40m')).toHaveAttribute('data-pulse', 'off')
  })

  /**
   * The urgency is still declared — a buyer who has asked for less motion still
   * needs to see that time is running out, in colour rather than movement.
   */
  it('does not pulse when the reader has asked for reduced motion', () => {
    stubMediaPreferences({ prefersReducedMotion: true })

    render(<Countdown urgency="critical">0m 8s</Countdown>)

    const timer = screen.getByText('0m 8s')
    expect(timer).toHaveAttribute('data-pulse', 'off')
    expect(timer).toHaveAttribute('data-urgency', 'critical')
  })
})

describe('SaleCountdown', () => {
  // The sale countdowns are read against a pinned clock, so "3 minutes left"
  // stays 3 minutes left however long the suite takes to get here.
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('counts a live window down to its close, loudly as it nears', () => {
    vi.setSystemTime(new Date('2026-09-15T17:57:00Z'))

    render(<SaleCountdown flight={aFlight()} />)

    const timer = screen.getByText(/Ends in/)
    expect(timer).toHaveTextContent('Ends in 3m 0s')
    expect(timer).toHaveAttribute('data-urgency', 'critical')
  })

  it('counts an upcoming window down to its opening without shouting', () => {
    vi.setSystemTime(new Date('2026-09-15T09:59:00Z'))

    render(<SaleCountdown flight={aFlight({ saleState: 'Upcoming' })} />)

    const timer = screen.getByText(/Opens in/)
    expect(timer).toHaveTextContent('Opens in 1m 0s')
    expect(timer).toHaveAttribute('data-urgency', 'calm')
  })

  it('states plainly that an ended sale is over', () => {
    vi.setSystemTime(new Date('2026-09-15T19:00:00Z'))

    render(<SaleCountdown flight={aFlight({ saleState: 'Ended' })} />)

    expect(screen.getByText('Sale ended')).toHaveAttribute('data-urgency', 'elapsed')
  })
})
