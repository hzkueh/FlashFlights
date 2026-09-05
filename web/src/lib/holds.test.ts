import { afterEach, describe, expect, it, vi } from 'vitest'

import { HOLDS_PATH, type Hold, createHold } from '@/lib/holds'
import { stubFetch } from '@/test/backend'

/**
 * The SPA's side of Ordering's Hold endpoint. These assert the mapping from each
 * status Ordering answers with onto the outcome the checkout branches on — the
 * distinction the whole flow rests on being that a conflict (someone else won
 * the Seats) is a different thing from a malformed request or an unreachable
 * gateway, and the buyer is told a different thing for each.
 */

const FLIGHT_ID = '0199f0e2-1111-7000-8000-000000000001'
const TOKEN = 'a-token-the-gateway-signed'

function aHold(overrides: Partial<Hold> = {}): Hold {
  return {
    holdId: '0199f0e2-2222-7000-8000-000000000001',
    flightId: FLIGHT_ID,
    userId: '0199f0e2-0000-7000-8000-000000000001',
    expiresAt: new Date(Date.now() + 2 * 60 * 1000).toISOString(),
    pricePerSeat: 49.99,
    seats: [{ seatId: 's-1', seatNumber: '1A' }],
    ...overrides,
  }
}

const request = { flightId: FLIGHT_ID, seatIds: ['s-1'], pricePerSeat: 49.99 }

afterEach(() => vi.unstubAllGlobals())

describe('createHold', () => {
  it('sends the flight, seats and price as the signed-in buyer', async () => {
    const hold = aHold()
    stubFetch(async () => Response.json(hold, { status: 201 }))

    const outcome = await createHold(request, TOKEN)

    expect(outcome).toEqual({ status: 'granted', hold })
    const [path, init] = vi.mocked(fetch).mock.calls[0]
    expect(path).toBe(HOLDS_PATH)
    expect(init).toBeDefined()
    expect(init!.method).toBe('POST')
    expect((init!.headers as Record<string, string>).authorization).toBe(`Bearer ${TOKEN}`)
    expect(JSON.parse(init!.body as string)).toEqual({
      flightId: FLIGHT_ID,
      seatIds: ['s-1'],
      pricePerSeat: 49.99,
    })
  })

  it('reports a conflict with the seats that blocked it, so the buyer knows which to give up', async () => {
    const seats = [{ seatId: 's-1', seatNumber: '1A', status: 'Held' }]
    stubFetch(async () =>
      Response.json(
        { title: 'Seats no longer available', detail: 'These seats are already held or booked: 1A.', seats },
        { status: 409 },
      ),
    )

    const outcome = await createHold(request, TOKEN)

    expect(outcome).toEqual({ status: 'conflict', seats })
  })

  it('reports a malformed request as invalid, distinct from a lost race', async () => {
    stubFetch(async () =>
      Response.json(
        { title: 'One or more validation errors occurred.', errors: { seatIds: ['At least one seat is required.'] } },
        { status: 400 },
      ),
    )

    const outcome = await createHold(request, TOKEN)

    expect(outcome.status).toBe('invalid')
  })

  it('reports the gateway being unreachable, distinct from any refusal', async () => {
    stubFetch(async () => {
      throw new TypeError('Failed to fetch')
    })

    const outcome = await createHold(request, TOKEN)

    expect(outcome.status).toBe('error')
  })

  it('propagates an abort rather than swallowing it as an error', async () => {
    const controller = new AbortController()
    controller.abort()
    stubFetch(async (_input, init) => {
      if (init?.signal?.aborted) throw new DOMException('Aborted', 'AbortError')
      return Response.json(aHold(), { status: 201 })
    })

    await expect(createHold(request, TOKEN, controller.signal)).rejects.toThrow(/abort/i)
  })
})
