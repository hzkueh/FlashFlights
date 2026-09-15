import { afterEach, describe, expect, it, vi } from 'vitest'

import { UNREACHABLE } from '@/lib/auth'
import {
  NOTIFICATION_PATHS,
  WATCH_FAILED,
  listWatchedFlightIds,
  markNotificationRead,
  readInbox,
  unwatchFlight,
  watchFlight,
} from '@/lib/notifications'
import { routed, stubFetch, unreachable } from '@/test/backend'
import { aNotification, anInbox } from '@/test/session'

/**
 * The SPA's side of the watch and inbox endpoints. What matters here is which
 * request is sent and how each server answer is classified — the refusal that
 * means "this sale already opened" is a different thing to show than a gateway
 * that could not be reached.
 */
const TOKEN = 'a-token-the-gateway-signed'
const FLIGHT_ID = '0199f0e2-1111-7000-8000-000000000001'

afterEach(() => vi.unstubAllGlobals())

describe('watching a flight', () => {
  it('asserts the watch as a state, addressed to the flight', async () => {
    const seen: Request[] = []
    stubFetch(
      routed({
        [NOTIFICATION_PATHS.watch(FLIGHT_ID)]: (request) => {
          seen.push(request)
          return new Response(null, { status: 204 })
        },
      }),
    )

    expect(await watchFlight(FLIGHT_ID, TOKEN)).toEqual({ status: 'done' })

    const [request] = seen
    expect(request.method).toBe('PUT')
    expect(request.headers.get('authorization')).toBe(`Bearer ${TOKEN}`)
  })

  it('un-watching sends the same path with DELETE', async () => {
    const seen: Request[] = []
    stubFetch(
      routed({
        [NOTIFICATION_PATHS.watch(FLIGHT_ID)]: (request) => {
          seen.push(request)
          return new Response(null, { status: 204 })
        },
      }),
    )

    expect(await unwatchFlight(FLIGHT_ID, TOKEN)).toEqual({ status: 'done' })
    expect(seen[0]?.method).toBe('DELETE')
  })

  it('reports a sale that has already started as its own outcome', async () => {
    stubFetch(
      routed({
        [NOTIFICATION_PATHS.watch(FLIGHT_ID)]: async () =>
          Response.json({ title: 'Conflict' }, { status: 409 }),
      }),
    )

    expect(await watchFlight(FLIGHT_ID, TOKEN)).toEqual({ status: 'saleStarted' })
  })

  it('tells an unreachable gateway apart from a refusal', async () => {
    stubFetch(unreachable)

    expect(await watchFlight(FLIGHT_ID, TOKEN)).toEqual({ status: 'error', message: UNREACHABLE })
  })

  it('reports any other refusal as a plain failure', async () => {
    stubFetch(
      routed({
        [NOTIFICATION_PATHS.watch(FLIGHT_ID)]: async () => new Response(null, { status: 500 }),
      }),
    )

    expect(await watchFlight(FLIGHT_ID, TOKEN)).toEqual({ status: 'error', message: WATCH_FAILED })
  })
})

describe('the watch list', () => {
  it('returns the flight ids the server names', async () => {
    stubFetch(
      routed({
        [NOTIFICATION_PATHS.watches]: async () => Response.json({ flightIds: [FLIGHT_ID] }),
      }),
    )

    expect(await listWatchedFlightIds(TOKEN)).toEqual([FLIGHT_ID])
  })

  it('reads as empty when the server sends no ids at all', async () => {
    stubFetch(routed({ [NOTIFICATION_PATHS.watches]: async () => Response.json({}) }))

    expect(await listWatchedFlightIds(TOKEN)).toEqual([])
  })
})

describe('the inbox', () => {
  it('returns the alerts and the unread count the server sent', async () => {
    const inbox = anInbox([aNotification()])
    stubFetch(routed({ [NOTIFICATION_PATHS.inbox]: async () => Response.json(inbox) }))

    expect(await readInbox(TOKEN)).toEqual(inbox)
  })

  it('fails loudly when the inbox cannot be read', async () => {
    stubFetch(routed({ [NOTIFICATION_PATHS.inbox]: async () => new Response(null, { status: 500 }) }))

    await expect(readInbox(TOKEN)).rejects.toThrow('Inbox returned 500')
  })

  it('marks one alert read as the token bearer', async () => {
    const seen: Request[] = []
    stubFetch(
      routed({
        [NOTIFICATION_PATHS.markRead('alert-1')]: (request) => {
          seen.push(request)
          return new Response(null, { status: 204 })
        },
      }),
    )

    expect(await markNotificationRead('alert-1', TOKEN)).toBe(true)
    expect(seen[0]?.headers.get('authorization')).toBe(`Bearer ${TOKEN}`)
  })

  /**
   * Someone else's alert — or one that is simply gone — is an answer, not a
   * failure: the server scopes the inbox to the token, so from out here it does
   * not exist.
   */
  it('reports an alert that is not this user’s as not found', async () => {
    stubFetch(
      routed({
        [NOTIFICATION_PATHS.markRead('alert-1')]: async () => new Response(null, { status: 404 }),
      }),
    )

    expect(await markNotificationRead('alert-1', TOKEN)).toBe(false)
  })
})
