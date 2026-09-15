import { UNREACHABLE, bearer } from '@/lib/auth'

/**
 * The SPA's side of Notifications' watch and inbox endpoints. Root-relative like
 * every other backend call (see `gateway.ts`), and authenticated throughout: a
 * Watch and an inbox belong to one person, and the server scopes both to the
 * token rather than to anything the client sends.
 */

export const NOTIFICATION_PATHS = {
  watches: '/api/notifications/watches',
  watch: (flightId: string) => `/api/notifications/watches/${flightId}`,
  inbox: '/api/notifications/inbox',
  markRead: (notificationId: string) => `/api/notifications/inbox/${notificationId}/read`,
} as const

/** One alert, in the shape Notifications serialises whether it arrives live or from the inbox. */
export interface Notification {
  id: string
  /** The flight whose sale opened, so the alert can link to it. */
  flightId: string
  body: string
  createdAt: string
  /** Null while unread — the unread count is a count of nulls. */
  readAt: string | null
}

/**
 * A user's inbox. The count comes from the server alongside the list rather than
 * being derived from it here, so the shell's badge and the page cannot disagree.
 */
export interface Inbox {
  notifications: Notification[]
  unreadCount: number
}

/** An empty inbox — what a signed-out visitor has, and the starting point for a signed-in one. */
export const EMPTY_INBOX: Inbox = { notifications: [], unreadCount: 0 }

/**
 * The three ways a watch write can end, shared by watching and un-watching.
 * `saleStarted` is the one worth its own case: a Watch fires only when a sale
 * opens, so once that moment has passed there is nothing left to wait for and
 * the server refuses.
 */
export type WatchOutcome =
  | { status: 'done' }
  | { status: 'saleStarted' }
  | { status: 'error'; message: string }

/** Shown when watching failed for a reason the SPA cannot name precisely. */
export const WATCH_FAILED = 'Something went wrong. Please try again.'

/** The flights this user is watching. Ids only — the catalog owns everything else about them. */
export async function listWatchedFlightIds(token: string, signal?: AbortSignal): Promise<string[]> {
  const response = await fetch(NOTIFICATION_PATHS.watches, {
    headers: { accept: 'application/json', ...bearer(token) },
    signal,
  })

  if (!response.ok) {
    throw new Error(`Watches returned ${response.status}`)
  }

  const body = (await response.json()) as { flightIds?: string[] }

  return body.flightIds ?? []
}

/**
 * Asks to be told when this flight's sale opens. PUT, because watching is a
 * state being asserted rather than an event appended — sending it twice leaves
 * the same single subscription.
 */
export async function watchFlight(flightId: string, token: string): Promise<WatchOutcome> {
  return send(NOTIFICATION_PATHS.watch(flightId), 'PUT', token)
}

/** Stops future alerts for this flight. Succeeds whether or not a watch was there. */
export async function unwatchFlight(flightId: string, token: string): Promise<WatchOutcome> {
  return send(NOTIFICATION_PATHS.watch(flightId), 'DELETE', token)
}

/** This user's alerts, newest first, with the unread tally the shell shows. */
export async function readInbox(token: string, signal?: AbortSignal): Promise<Inbox> {
  const response = await fetch(NOTIFICATION_PATHS.inbox, {
    headers: { accept: 'application/json', ...bearer(token) },
    signal,
  })

  if (!response.ok) {
    throw new Error(`Inbox returned ${response.status}`)
  }

  return (await response.json()) as Inbox
}

/**
 * Marks one alert read. Resolves to false when there is no such alert for this
 * user — an answer, not a failure, so a stale list can be reconciled rather than
 * shown as broken.
 */
export async function markNotificationRead(notificationId: string, token: string): Promise<boolean> {
  const response = await fetch(NOTIFICATION_PATHS.markRead(notificationId), {
    method: 'POST',
    headers: bearer(token),
  })

  if (response.status === 404) {
    return false
  }

  if (!response.ok) {
    throw new Error(`Marking read returned ${response.status}`)
  }

  return true
}

/**
 * The two watch writes differ only by method, and both answer the same three
 * ways. Never throws for a refusal — every server answer becomes an outcome the
 * toggle renders — matching how `createHold` treats Ordering's refusals.
 */
async function send(path: string, method: 'PUT' | 'DELETE', token: string): Promise<WatchOutcome> {
  let response: Response

  try {
    response = await fetch(path, { method, headers: bearer(token) })
  } catch {
    return { status: 'error', message: UNREACHABLE }
  }

  if (response.ok) {
    return { status: 'done' }
  }

  // The sale opened while this page was on screen. The server is right and the
  // page is stale, so the toggle says so rather than retrying.
  if (response.status === 409) {
    return { status: 'saleStarted' }
  }

  return { status: 'error', message: WATCH_FAILED }
}
