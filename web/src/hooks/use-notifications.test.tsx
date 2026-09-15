import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { SessionProvider } from '@/hooks/use-session'
import { NOTIFICATION_PATHS, type Notification } from '@/lib/notifications'
import type { NotificationConnectionFactory } from '@/lib/notifications-live'
import { type Route, stubFetch } from '@/test/backend'
import { aNotification, anInbox, signedInGateway, storeSession } from '@/test/session'
import { NotificationsProvider, useNotifications } from './use-notifications'

/**
 * The inbox the whole app reads from: loaded once when a session appears, kept
 * current by the live hub, and cleared when there is no one signed in.
 */

/** A fake hub whose pushes and reconnects a test can drive, plus its factory. */
function fakeHub() {
  let handler: ((notification: Notification) => void) | undefined
  let reconnected: (() => void) | undefined

  const factory: NotificationConnectionFactory = () =>
    ({
      on: (_method: string, h: (notification: Notification) => void) => {
        handler = h
      },
      off: () => {
        handler = undefined
      },
      onreconnected: (h: () => void) => {
        reconnected = h
      },
      start: async () => {},
      stop: async () => {},
    }) as never

  return {
    factory,
    push: (notification: Notification) => handler?.(notification),
    reconnect: () => reconnected?.(),
  }
}

function renderInbox(routes: Record<string, Route> = {}, { signedIn = true } = {}) {
  if (signedIn) {
    storeSession()
  }

  const hub = fakeHub()
  stubFetch(signedInGateway(routes))

  const view = renderHook(() => useNotifications(), {
    wrapper: ({ children }) => (
      <SessionProvider>
        <NotificationsProvider createConnection={hub.factory}>{children}</NotificationsProvider>
      </SessionProvider>
    ),
  })

  return { ...view, hub }
}

afterEach(() => {
  localStorage.clear()
  vi.unstubAllGlobals()
})

describe('the notifications provider', () => {
  it('loads the inbox and its unread count for a signed-in user', async () => {
    const { result } = renderInbox({
      [NOTIFICATION_PATHS.inbox]: async () =>
        Response.json(anInbox([aNotification({ id: 'alert-1' })])),
    })

    await waitFor(() => expect(result.current.notifications).toHaveLength(1))
    expect(result.current.unreadCount).toBe(1)
  })

  /**
   * The alerts belong to a user, so there is no inbox at all without one —
   * signing out must not leave someone else's alerts on screen.
   */
  it('has no inbox at all while signed out', async () => {
    const { result } = renderInbox({}, { signedIn: false })

    await waitFor(() => expect(result.current.notifications).toEqual([]))
    expect(result.current.unreadCount).toBe(0)
  })

  it('folds a live alert in at the top and counts it as unread', async () => {
    const { result, hub } = renderInbox({
      [NOTIFICATION_PATHS.inbox]: async () =>
        Response.json(anInbox([aNotification({ id: 'alert-1', body: 'FF001 is now on sale' })])),
    })

    await waitFor(() => expect(result.current.notifications).toHaveLength(1))

    act(() => hub.push(aNotification({ id: 'alert-2', body: 'FF002 is now on sale' })))

    expect(result.current.notifications.map((notification) => notification.id)).toEqual([
      'alert-2',
      'alert-1',
    ])
    expect(result.current.unreadCount).toBe(2)
  })

  /**
   * A push can arrive for an alert the inbox read already returned. Showing it
   * twice — or counting it twice — would be a visible lie about how many alerts
   * there are.
   */
  it('does not show an alert twice when a push repeats one it already has', async () => {
    const { result, hub } = renderInbox({
      [NOTIFICATION_PATHS.inbox]: async () => Response.json(anInbox([aNotification({ id: 'alert-1' })])),
    })

    await waitFor(() => expect(result.current.notifications).toHaveLength(1))

    act(() => hub.push(aNotification({ id: 'alert-1' })))

    expect(result.current.notifications).toHaveLength(1)
    expect(result.current.unreadCount).toBe(1)
  })

  it('marks an alert read and drops the unread count', async () => {
    const marked: string[] = []
    const { result } = renderInbox({
      [NOTIFICATION_PATHS.inbox]: async () => Response.json(anInbox([aNotification({ id: 'alert-1' })])),
      [NOTIFICATION_PATHS.markRead('alert-1')]: async () => {
        marked.push('alert-1')
        return new Response(null, { status: 204 })
      },
    })

    await waitFor(() => expect(result.current.unreadCount).toBe(1))

    await act(() => result.current.markRead('alert-1'))

    expect(result.current.unreadCount).toBe(0)
    expect(result.current.notifications[0]?.readAt).not.toBeNull()
    expect(marked).toEqual(['alert-1'])
  })

  it('marking the same alert read twice does not drive the count below zero', async () => {
    const { result } = renderInbox({
      [NOTIFICATION_PATHS.inbox]: async () => Response.json(anInbox([aNotification({ id: 'alert-1' })])),
      [NOTIFICATION_PATHS.markRead('alert-1')]: async () => new Response(null, { status: 204 }),
    })

    await waitFor(() => expect(result.current.unreadCount).toBe(1))

    await act(() => result.current.markRead('alert-1'))
    await act(() => result.current.markRead('alert-1'))

    expect(result.current.unreadCount).toBe(0)
  })

  /**
   * Alerts that fired while the connection was down were never pushed, so a
   * recovered connection re-reads rather than resumes.
   */
  it('re-reads the inbox after a dropped connection recovers', async () => {
    let reads = 0
    const { result, hub } = renderInbox({
      [NOTIFICATION_PATHS.inbox]: async () => {
        reads += 1
        return Response.json(
          anInbox(
            reads === 1 ? [] : [aNotification({ id: 'alert-missed', body: 'FF003 is now on sale' })],
          ),
        )
      },
    })

    await waitFor(() => expect(reads).toBe(1))

    act(() => hub.reconnect())

    await waitFor(() => expect(result.current.notifications).toHaveLength(1))
    expect(result.current.notifications[0]?.id).toBe('alert-missed')
  })

  /** Being offline is not the same as having no alerts. */
  it('keeps the last-known inbox when the gateway cannot be reached', async () => {
    const { result } = renderInbox({
      [NOTIFICATION_PATHS.inbox]: async () => new Response(null, { status: 503 }),
    })

    await waitFor(() => expect(result.current.notifications).toEqual([]))
    expect(result.current.unreadCount).toBe(0)
  })
})
