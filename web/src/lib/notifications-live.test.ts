import { describe, expect, it, vi } from 'vitest'

import type { Notification } from '@/lib/notifications'
import {
  NOTIFICATIONS_HUB_PATH,
  type NotificationConnectionFactory,
  subscribeToNotifications,
} from '@/lib/notifications-live'
import { aNotification } from '@/test/session'

/**
 * The live half of the inbox. There is no subscribe call to make here — the
 * server addresses an alert to its own user from the connection's token — so
 * what needs proving is that the connection is opened as that user, that alerts
 * reach the handler, and that disposing cannot strand a half-open connection.
 */
const TOKEN = 'a-token-the-gateway-signed'

/** A fake connection whose pushes and reconnects a test can drive, plus its factory. */
function fakeFactory() {
  const opened: { hubPath: string; token: string }[] = []
  let handler: ((notification: Notification) => void) | undefined
  let reconnected: (() => void) | undefined
  const stop = vi.fn(async () => {})
  const start = vi.fn(async () => {})

  const factory: NotificationConnectionFactory = (hubPath, token) => {
    opened.push({ hubPath, token })

    return {
      on: (_method: string, h: (notification: Notification) => void) => {
        handler = h
      },
      off: () => {
        handler = undefined
      },
      onreconnected: (h: () => void) => {
        reconnected = h
      },
      start,
      stop,
    } as never
  }

  return {
    factory,
    opened,
    start,
    stop,
    push: (notification: Notification) => handler?.(notification),
    reconnect: () => reconnected?.(),
    hasHandler: () => handler !== undefined,
  }
}

describe('subscribeToNotifications', () => {
  it('opens the hub as the signed-in user', () => {
    const fake = fakeFactory()

    subscribeToNotifications(TOKEN, () => {}, { createConnection: fake.factory })

    expect(fake.opened).toEqual([{ hubPath: NOTIFICATIONS_HUB_PATH, token: TOKEN }])
    expect(fake.start).toHaveBeenCalled()
  })

  it('hands each pushed alert to the caller', () => {
    const fake = fakeFactory()
    const received: Notification[] = []

    subscribeToNotifications(TOKEN, (notification) => received.push(notification), {
      createConnection: fake.factory,
    })

    const notification = aNotification()
    fake.push(notification)

    expect(received).toEqual([notification])
  })

  /**
   * Alerts that fired while the connection was down were never pushed, so a
   * recovered connection has to catch up from the inbox rather than resume.
   */
  it('asks the caller to re-sync after a reconnect', () => {
    const fake = fakeFactory()
    const resynced = vi.fn()

    subscribeToNotifications(TOKEN, () => {}, {
      createConnection: fake.factory,
      onReconnected: resynced,
    })

    fake.reconnect()

    expect(resynced).toHaveBeenCalledTimes(1)
  })

  it('stops listening and closes the connection when disposed', async () => {
    const fake = fakeFactory()

    const subscription = subscribeToNotifications(TOKEN, () => {}, { createConnection: fake.factory })
    await subscription.dispose()

    expect(fake.hasHandler()).toBe(false)
    expect(fake.stop).toHaveBeenCalled()
  })

  /**
   * The live channel is the fast path, never the record: every alert is
   * persisted before it is pushed, so a connection that cannot be built at all
   * costs immediacy and nothing else.
   */
  it('falls back silently when a connection cannot be built', async () => {
    const subscription = subscribeToNotifications(TOKEN, () => {}, {
      createConnection: () => {
        throw new Error('No SignalR here.')
      },
    })

    await subscription.dispose()
  })
})
