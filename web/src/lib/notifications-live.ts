/**
 * The SPA's live half of the notification inbox. The inbox is read once over
 * HTTP (see `notifications.ts`); this module keeps it current after that by
 * holding a SignalR connection to Notifications open, so an alert that fires
 * while the app is on screen arrives without a refresh.
 *
 * Unlike the seat-map hub — anonymous, and joined per flight — this one is
 * authenticated and needs no subscribe call: the server knows whose connection
 * it is from the token and addresses each alert to its own user. There is
 * deliberately nothing to ask for, so a client cannot name whose alerts it
 * would like.
 *
 * The push is the fast path, never the record. Every alert is persisted before
 * it is pushed, so a connection that never opens, drops, or misses a message
 * costs only immediacy — the alert is still in the inbox on the next read.
 */
import { type HubConnection, HubConnectionBuilder } from '@microsoft/signalr'

import type { Notification } from '@/lib/notifications'

/** Reached through the gateway, same-origin behind it, like every other SPA call. */
export const NOTIFICATIONS_HUB_PATH = '/api/notifications/hubs/notifications'

/** The name shared with `NotificationsHub` on the server — the client/server contract. */
const NOTIFICATION_RECEIVED = 'NotificationReceived'

/** A live subscription; dispose to close the connection. */
export interface NotificationSubscription {
  dispose: () => Promise<void>
}

/** Builds the default SignalR connection; swapped for a fake in tests. */
export type NotificationConnectionFactory = (hubPath: string, token: string) => HubConnection

const defaultConnectionFactory: NotificationConnectionFactory = (hubPath, token) =>
  new HubConnectionBuilder()
    // A browser cannot put an Authorization header on a WebSocket handshake, so
    // the client sends the token as a query parameter and the server reads it
    // there for this path alone. A factory, not a value: it is re-read on every
    // reconnect, so a connection that recovers after a token change carries the
    // current one.
    .withUrl(hubPath, { accessTokenFactory: () => token })
    // A dropped connection is retried on its own rather than leaving the inbox
    // frozen at whatever it last saw.
    .withAutomaticReconnect()
    .build()

export interface NotificationSubscriptionOptions {
  /** Swaps the SignalR connection for a fake in tests. */
  createConnection?: NotificationConnectionFactory
  /**
   * Called after a dropped connection has recovered. The caller re-reads the
   * inbox here: alerts that fired while disconnected were never pushed, so the
   * list must be re-synced to true state rather than merely resumed.
   */
  onReconnected?: () => void
}

/**
 * Opens a connection as the given user and calls `onNotification` for each alert
 * until disposed. Failures to connect are swallowed — the live channel is the
 * fast path, so a user keeps the inbox they already read rather than seeing an
 * error for something that costs them nothing.
 */
export function subscribeToNotifications(
  token: string,
  onNotification: (notification: Notification) => void,
  options: NotificationSubscriptionOptions = {},
): NotificationSubscription {
  const { createConnection = defaultConnectionFactory, onReconnected } = options

  let connection: HubConnection
  try {
    connection = createConnection(NOTIFICATIONS_HUB_PATH, token)
  } catch {
    // Couldn't even build a connection (e.g. no browser to host one). The inbox
    // read still works, so fall back to it rather than failing to render.
    return { dispose: async () => {} }
  }

  connection.on(NOTIFICATION_RECEIVED, onNotification)

  // Nothing to re-subscribe to: the server addresses this connection by its
  // token, so a recovered connection is already receiving again. All the caller
  // has to do is catch up on what it missed while it was gone.
  connection.onreconnected(() => onReconnected?.())

  const ready = connection.start().catch(() => {})

  return {
    dispose: async () => {
      connection.off(NOTIFICATION_RECEIVED, onNotification)
      // Await the handshake so dispose cannot race a stop ahead of the start it
      // would otherwise strand.
      await ready
      await connection.stop()
    },
  }
}
