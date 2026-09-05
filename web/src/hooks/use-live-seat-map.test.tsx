import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { Seat, SeatMap } from '@/lib/seat-map'
import type { ConnectionFactory, SeatMapChange } from '@/lib/seat-map-live'
import { useLiveSeatMap } from './use-live-seat-map'

function seat(seatId: string, status: Seat['status']): Seat {
  return { seatId, seatNumber: seatId, row: 1, column: seatId, status }
}

/** A fake connection whose pushes and reconnects a test can drive, plus its factory. */
function fakeFactory() {
  let handler: ((change: SeatMapChange) => void) | undefined
  let reconnected: (() => void | Promise<void>) | undefined

  const factory: ConnectionFactory = () =>
    ({
      on: (_method: string, h: (change: SeatMapChange) => void) => {
        handler = h
      },
      off: () => {
        handler = undefined
      },
      onreconnected: (h: () => void | Promise<void>) => {
        reconnected = h
      },
      start: async () => {},
      invoke: async () => {},
      stop: async () => {},
    }) as never

  return {
    factory,
    push: (change: SeatMapChange) => handler?.(change),
    reconnect: async () => reconnected?.(),
  }
}

describe('useLiveSeatMap', () => {
  it('starts from the map read over HTTP', () => {
    const { factory } = fakeFactory()
    const initial = [seat('a', 'Available')]

    const { result } = renderHook(() => useLiveSeatMap('flight-1', initial, { createConnection: factory }))

    expect(result.current).toEqual(initial)
  })

  it('repaints a seat when a change is pushed — no refresh', () => {
    const { factory, push } = fakeFactory()
    const initial = [seat('a', 'Available'), seat('b', 'Available')]

    const { result } = renderHook(() => useLiveSeatMap('flight-1', initial, { createConnection: factory }))

    act(() =>
      push({ flightId: 'flight-1', occurredAt: '2026-09-06T12:00:00Z', seats: [{ seatId: 'b', status: 'Held' }] }),
    )

    expect(result.current.map((s) => s.status)).toEqual(['Available', 'Held'])
  })

  it('re-seeds when the initial map is re-read', () => {
    const { factory, push } = fakeFactory()

    const { result, rerender } = renderHook(
      ({ seats }) => useLiveSeatMap('flight-1', seats, { createConnection: factory }),
      { initialProps: { seats: [seat('a', 'Available')] } },
    )

    act(() =>
      push({ flightId: 'flight-1', occurredAt: '2026-09-06T12:00:00Z', seats: [{ seatId: 'a', status: 'Held' }] }),
    )
    expect(result.current[0].status).toBe('Held')

    // A fresh read supersedes the live edits it predates.
    rerender({ seats: [seat('a', 'Confirmed')] })
    expect(result.current[0].status).toBe('Confirmed')
  })

  it('re-syncs to the authoritative map after a reconnect', async () => {
    const { factory, reconnect } = fakeFactory()
    const initial = [seat('a', 'Available'), seat('b', 'Available')]

    // While disconnected, seat b was confirmed and seat a held — changes the map
    // never received. The re-read returns that true state.
    const trueState: SeatMap = {
      flightId: 'flight-1',
      seats: [seat('a', 'Held'), seat('b', 'Confirmed')],
    }
    const resync = vi.fn(async () => trueState)

    const { result } = renderHook(() =>
      useLiveSeatMap('flight-1', initial, { createConnection: factory, resync }),
    )

    await act(async () => {
      await reconnect()
    })

    expect(resync).toHaveBeenCalledWith('flight-1')
    await waitFor(() => expect(result.current.map((s) => s.status)).toEqual(['Held', 'Confirmed']))
  })
})
