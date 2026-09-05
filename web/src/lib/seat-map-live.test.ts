import { describe, expect, it, vi } from 'vitest'

import type { Seat } from '@/lib/seat-map'
import {
  SEAT_MAP_HUB_PATH,
  type SeatMapChange,
  applySeatChanges,
  subscribeToSeatMap,
} from '@/lib/seat-map-live'

function seat(seatId: string, status: Seat['status']): Seat {
  return { seatId, seatNumber: seatId, row: 1, column: seatId, status }
}

describe('applySeatChanges', () => {
  it('repaints only the seats named, matched by id', () => {
    const seats = [seat('a', 'Available'), seat('b', 'Available'), seat('c', 'Available')]

    const next = applySeatChanges(seats, {
      flightId: 'f',
      occurredAt: '2026-09-06T12:00:00Z',
      seats: [
        { seatId: 'a', status: 'Held' },
        { seatId: 'c', status: 'Confirmed' },
      ],
    })

    expect(next.map((s) => s.status)).toEqual(['Held', 'Available', 'Confirmed'])
  })

  it('keeps the label and grid position, changing only status', () => {
    const original = seat('12A', 'Available')

    const [next] = applySeatChanges([original], {
      flightId: 'f',
      occurredAt: '2026-09-06T12:00:00Z',
      seats: [{ seatId: '12A', status: 'Held' }],
    })

    expect(next).toEqual({ ...original, status: 'Held' })
  })

  it('ignores a change for a seat not in the cabin', () => {
    const seats = [seat('a', 'Available')]

    const next = applySeatChanges(seats, {
      flightId: 'f',
      occurredAt: '2026-09-06T12:00:00Z',
      seats: [{ seatId: 'ghost', status: 'Held' }],
    })

    expect(next).toEqual(seats)
  })

  it('returns the same array reference when nothing changed', () => {
    const seats = [seat('a', 'Held')]

    expect(applySeatChanges(seats, { flightId: 'f', occurredAt: '', seats: [] })).toBe(seats)
  })
})

/**
 * A fake HubConnection standing in for @microsoft/signalr — records the handler,
 * the calls made, and lets a test drive a pushed message. Only the members
 * `subscribeToSeatMap` touches are implemented.
 */
function fakeConnection() {
  const handlers = new Map<string, (change: SeatMapChange) => void>()
  const invocations: Array<[string, unknown]> = []
  let started = false
  let stopped = false

  const connection = {
    on: (method: string, handler: (change: SeatMapChange) => void) => handlers.set(method, handler),
    off: (method: string) => handlers.delete(method),
    start: vi.fn(async () => {
      started = true
    }),
    invoke: vi.fn(async (method: string, arg: unknown) => {
      invocations.push([method, arg])
    }),
    stop: vi.fn(async () => {
      stopped = true
    }),
  }

  return {
    connection,
    push: (change: SeatMapChange) => handlers.get('SeatsChanged')?.(change),
    invocations,
    isStarted: () => started,
    isStopped: () => stopped,
    hasHandler: () => handlers.has('SeatsChanged'),
  }
}

describe('subscribeToSeatMap', () => {
  it('connects to the hub behind the gateway and subscribes to the one flight', async () => {
    const fake = fakeConnection()
    let url = ''

    subscribeToSeatMap('flight-1', () => {}, (hubPath) => {
      url = hubPath
      return fake.connection as never
    })

    await vi.waitFor(() => expect(fake.invocations).toContainEqual(['Subscribe', 'flight-1']))
    expect(url).toBe(SEAT_MAP_HUB_PATH)
    expect(fake.isStarted()).toBe(true)
  })

  it('delivers pushed changes to the caller', async () => {
    const fake = fakeConnection()
    const changes: SeatMapChange[] = []

    subscribeToSeatMap('flight-1', (change) => changes.push(change), () => fake.connection as never)

    const change: SeatMapChange = {
      flightId: 'flight-1',
      occurredAt: '2026-09-06T12:00:00Z',
      seats: [{ seatId: 'a', status: 'Held' }],
    }
    fake.push(change)

    expect(changes).toEqual([change])
  })

  it('detaches the handler and stops the connection on dispose', async () => {
    const fake = fakeConnection()

    const subscription = subscribeToSeatMap('flight-1', () => {}, () => fake.connection as never)
    await subscription.dispose()

    expect(fake.hasHandler()).toBe(false)
    expect(fake.isStopped()).toBe(true)
  })
})
