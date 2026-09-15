import { Link } from 'react-router'

import { StatePanel } from '@/components/state-panel'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { LoadingPanel, Skeleton } from '@/components/ui/skeleton'
import { useNotifications } from '@/hooks/use-notifications'
import { useSession } from '@/hooks/use-session'
import { formatDateTime } from '@/lib/format'
import type { Notification } from '@/lib/notifications'

/**
 * The persisted inbox: every alert this user has been sent, whether or not they
 * were connected when it fired. An alert that arrived live is already in this
 * list — the provider above folds pushes into the same state the read filled —
 * so there is one list here, not a live one beside a stored one.
 */
export function NotificationsPage() {
  const { session } = useSession()
  const { status, notifications, unreadCount, markRead } = useNotifications()

  if (session === null) {
    return (
      <section className="space-y-4">
        <h1 className="font-heading text-2xl font-semibold tracking-tight">Notifications</h1>
        <p className="text-muted-foreground text-sm">
          <Link to="/login" className="underline underline-offset-4 hover:text-foreground">
            Sign in
          </Link>{' '}
          to see your alerts.
        </p>
      </section>
    )
  }

  return (
    <section className="space-y-6">
      <header className="flex flex-wrap items-center gap-3">
        <h1 className="font-heading text-2xl font-semibold tracking-tight">Notifications</h1>
        {unreadCount > 0 && <Badge aria-label={`${unreadCount} unread`}>{unreadCount} unread</Badge>}
      </header>

      {status === 'loading' ? (
        <InboxSkeleton />
      ) : status === 'error' && notifications.length === 0 ? (
        // Only when there is nothing to fall back on. A re-read that fails while
        // alerts are already on screen leaves them there — an unreachable gateway
        // is not a reason to hide what this user has already been sent.
        <StatePanel tone="error" title="Couldn't load your alerts.">
          <p className="text-muted-foreground text-sm">
            Check the gateway is running, then try again.
          </p>
        </StatePanel>
      ) : notifications.length === 0 ? (
        <StatePanel title="No alerts yet.">
          <p className="text-muted-foreground text-sm">
            Watch an upcoming flash sale and you will be told the moment it opens.
          </p>
          <Button asChild variant="outline">
            <Link to="/">Browse flights</Link>
          </Button>
        </StatePanel>
      ) : (
        <ul className="divide-y rounded-lg border">
          {notifications.map((notification) => (
            <NotificationRow
              key={notification.id}
              notification={notification}
              onRead={() => void markRead(notification.id)}
            />
          ))}
        </ul>
      )}
    </section>
  )
}

/** Three rows' worth of inbox, so the page does not jump when the read lands. */
function InboxSkeleton() {
  return (
    <LoadingPanel label="Loading your alerts…" className="divide-y rounded-lg border">
      {[0, 1, 2].map((row) => (
        <div key={row} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
          <div className="space-y-2">
            <Skeleton className="h-4 w-56 max-w-full" />
            <Skeleton className="h-3 w-32" />
          </div>
          <Skeleton className="h-8 w-24" />
        </div>
      ))}
    </LoadingPanel>
  )
}

function NotificationRow({
  notification,
  onRead,
}: {
  notification: Notification
  onRead: () => void
}) {
  const unread = notification.readAt === null

  return (
    <li className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
      <div className="space-y-1">
        <p className={unread ? 'text-sm font-medium' : 'text-muted-foreground text-sm'}>
          {notification.body}
        </p>
        <p className="text-muted-foreground text-xs tabular-nums">
          {formatDateTime(notification.createdAt)}
        </p>
      </div>

      <div className="flex items-center gap-2">
        <Button asChild variant="outline" size="sm">
          <Link to={`/flights/${notification.flightId}`}>View flight</Link>
        </Button>

        {unread && (
          <Button type="button" variant="ghost" size="sm" onClick={onRead}>
            Mark read
          </Button>
        )}
      </div>
    </li>
  )
}
