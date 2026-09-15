import { createContext, use, useCallback, useEffect, useMemo, useState } from 'react'

import { useSession } from '@/hooks/use-session'
import {
  EMPTY_INBOX,
  type Inbox,
  type Notification,
  markNotificationRead,
  readInbox,
} from '@/lib/notifications'
import {
  type NotificationConnectionFactory,
  subscribeToNotifications,
} from '@/lib/notifications-live'

interface NotificationsContextValue {
  /** This user's alerts, newest first. Empty while signed out. */
  notifications: Notification[]
  /** How many are unread — what the shell's badge shows. */
  unreadCount: number
  /** Marks one alert read, locally and on the server. */
  markRead: (notificationId: string) => Promise<void>
}

const NotificationsContext = createContext<NotificationsContextValue | null>(null)

export interface NotificationsProviderProps {
  children: React.ReactNode
  /** Injects the SignalR connection for tests. */
  createConnection?: NotificationConnectionFactory
}

/**
 * Owns the notification inbox for whoever is signed in: read once when the
 * session appears, then kept current by the live hub.
 *
 * <p>Lives above the router rather than on the notifications page, because the
 * unread count belongs to the app shell — a user has to see that something
 * arrived without being on the page that lists it.</p>
 *
 * <p>Signed out, there is no inbox at all: the alerts belong to a user, so
 * signing out clears them here rather than leaving another person's alerts on
 * screen.</p>
 */
export function NotificationsProvider({ children, createConnection }: NotificationsProviderProps) {
  const { session } = useSession()
  const token = session?.token ?? null

  const [inbox, setInbox] = useState<Inbox>(EMPTY_INBOX)

  // Clear the inbox the moment the session changes, during render rather than in
  // an effect, so signing out — or signing in as someone else — never paints a
  // frame of the previous user's alerts before an effect corrects it. This is
  // React's documented "adjust state while rendering" pattern, the same one the
  // live seat map uses, tracking the session it last loaded for in state.
  const [loadedFor, setLoadedFor] = useState(token)
  if (loadedFor !== token) {
    setLoadedFor(token)
    setInbox(EMPTY_INBOX)
  }

  useEffect(() => {
    if (token === null) {
      return
    }

    const controller = new AbortController()

    // The read and the live channel are set up together: the read is the
    // authoritative list, and the subscription folds in anything that fires
    // after it. Both are torn down when the token changes or the app unmounts.
    const load = async () => {
      try {
        const loaded = await readInbox(token, controller.signal)

        if (!controller.signal.aborted) {
          setInbox(loaded)
        }
      } catch {
        // An unreachable gateway leaves the last-known inbox rather than
        // emptying it — being offline is not the same as having no alerts.
      }
    }

    void load()

    const subscription = subscribeToNotifications(
      token,
      (notification) =>
        setInbox((current) => ({
          // Newest first, and never twice: a redelivered push (or one that
          // arrives for an alert the read already returned) must not show up as
          // a second copy.
          notifications: [
            notification,
            ...current.notifications.filter((existing) => existing.id !== notification.id),
          ],
          unreadCount:
            current.notifications.some((existing) => existing.id === notification.id)
              ? current.unreadCount
              : current.unreadCount + 1,
        })),
      {
        createConnection,
        // A reconnect means alerts may have fired unheard, so the inbox is
        // re-read rather than resumed.
        onReconnected: () => void load(),
      },
    )

    return () => {
      controller.abort()
      void subscription.dispose()
    }
  }, [token, createConnection])

  const markRead = useCallback(
    async (notificationId: string) => {
      if (token === null) {
        return
      }

      // Marked locally first, so the badge responds to the click rather than to
      // the round trip. The server is idempotent, and a failure here leaves a
      // stale-read alert that the next inbox read corrects — the cheapest
      // possible wrong state.
      setInbox((current) => {
        const target = current.notifications.find((notification) => notification.id === notificationId)

        if (target === undefined || target.readAt !== null) {
          return current
        }

        return {
          notifications: current.notifications.map((notification) =>
            notification.id === notificationId
              ? { ...notification, readAt: new Date().toISOString() }
              : notification,
          ),
          unreadCount: Math.max(0, current.unreadCount - 1),
        }
      })

      try {
        await markNotificationRead(notificationId, token)
      } catch {
        // Swallowed: the alert is read as far as this user is concerned, and the
        // next inbox read settles it either way.
      }
    },
    [token],
  )

  const value = useMemo<NotificationsContextValue>(
    () => ({
      notifications: inbox.notifications,
      unreadCount: inbox.unreadCount,
      markRead,
    }),
    [inbox, markRead],
  )

  return <NotificationsContext value={value}>{children}</NotificationsContext>
}

/** Throws outside a {@link NotificationsProvider} rather than silently reporting an empty inbox. */
export function useNotifications(): NotificationsContextValue {
  const value = use(NotificationsContext)

  if (value === null) {
    throw new Error('useNotifications must be used within a NotificationsProvider')
  }

  return value
}
