import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { NOTIFICATION_PATHS } from '@/lib/notifications'
import { type Route } from '@/test/backend'
import { stubPrefersDark } from '@/test/match-media'
import { renderApp } from '@/test/render-app'
import { aNotification, anInbox, signedInGateway, storeSession } from '@/test/session'

/**
 * The notification inbox through the whole app: a signed-in user sees the alerts
 * they were sent whether or not they were connected when each fired, the app
 * shell shows how many are unread from any page, and reading one clears it.
 */
function renderNotifications(routes: Record<string, Route>, { signedIn = true, route = '/notifications' } = {}) {
  if (signedIn) {
    storeSession()
  }

  return renderApp(signedInGateway(routes), route)
}

beforeEach(() => {
  localStorage.clear()
  stubPrefersDark(false)
})

afterEach(() => vi.unstubAllGlobals())

describe('the notifications page', () => {
  /**
   * The whole point of the persisted inbox: this user was not connected when the
   * sale opened, and the alert is still here on their next visit.
   */
  it('shows an alert that fired while the user was away', async () => {
    renderNotifications({
      [NOTIFICATION_PATHS.inbox]: async () =>
        Response.json(anInbox([aNotification({ body: 'FF412 LHR to BCN is now on sale' })])),
    })

    expect(await screen.findByText('FF412 LHR to BCN is now on sale')).toBeInTheDocument()
  })

  it('links an alert to the flight whose sale opened', async () => {
    renderNotifications({
      [NOTIFICATION_PATHS.inbox]: async () =>
        Response.json(anInbox([aNotification({ flightId: 'flight-9' })])),
    })

    expect(await screen.findByRole('link', { name: 'View flight' })).toHaveAttribute(
      'href',
      '/flights/flight-9',
    )
  })

  it('says so plainly when there are no alerts yet', async () => {
    renderNotifications({ [NOTIFICATION_PATHS.inbox]: async () => Response.json(anInbox()) })

    expect(await screen.findByText(/No alerts yet/)).toBeInTheDocument()
  })

  /**
   * An empty inbox and an unreachable one are different answers, and only one of
   * them means "nothing has happened yet". Telling a user who has alerts waiting
   * that they have none is the wrong kind of wrong.
   */
  it('says the read failed rather than claiming there are no alerts', async () => {
    renderNotifications({
      [NOTIFICATION_PATHS.inbox]: () => {
        throw new TypeError('Failed to fetch')
      },
    })

    expect(await screen.findByRole('alert')).toHaveTextContent(/Couldn't load your alerts/)
    expect(screen.queryByText(/No alerts yet/)).not.toBeInTheDocument()
  })

  it('prompts a signed-out visitor to sign in rather than showing an empty inbox', async () => {
    renderNotifications({}, { signedIn: false })

    // Scoped to the page: the header carries its own sign-in link, and this is
    // about what the inbox says, not the shell around it.
    const page = within(await screen.findByRole('main'))

    expect(await page.findByRole('link', { name: 'Sign in' })).toBeInTheDocument()
    expect(page.queryByText(/No alerts yet/)).not.toBeInTheDocument()
  })

  it('marks an alert read, and stops offering to', async () => {
    const marked: string[] = []
    renderNotifications({
      [NOTIFICATION_PATHS.inbox]: async () =>
        Response.json(anInbox([aNotification({ id: 'alert-1' })])),
      [NOTIFICATION_PATHS.markRead('alert-1')]: async () => {
        marked.push('alert-1')
        return new Response(null, { status: 204 })
      },
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Mark read' }))

    expect(marked).toEqual(['alert-1'])
    expect(screen.queryByRole('button', { name: 'Mark read' })).not.toBeInTheDocument()
  })
})

describe('the unread count in the app shell', () => {
  /**
   * The badge is the reason a user looks at the page at all, so it has to be
   * visible from wherever they happen to be — not only once they are on it.
   */
  it('is visible from another page entirely', async () => {
    renderNotifications(
      {
        [NOTIFICATION_PATHS.inbox]: async () =>
          Response.json(anInbox([aNotification({ id: 'a' }), aNotification({ id: 'b' })])),
      },
      { route: '/bookings' },
    )

    const link = await screen.findByRole('link', { name: /Notifications/ })
    expect(await within(link).findByLabelText('2 unread notifications')).toHaveTextContent('2')
  })

  it('counts only what is unread', async () => {
    renderNotifications({
      [NOTIFICATION_PATHS.inbox]: async () =>
        Response.json(
          anInbox([aNotification({ id: 'a' }), aNotification({ id: 'b', readAt: '2026-09-15T13:00:00Z' })]),
        ),
    })

    const link = await screen.findByRole('link', { name: /Notifications/ })
    expect(await within(link).findByLabelText('1 unread notification')).toBeInTheDocument()
  })

  it('shows nothing at all when everything has been read', async () => {
    renderNotifications({
      [NOTIFICATION_PATHS.inbox]: async () =>
        Response.json(anInbox([aNotification({ readAt: '2026-09-15T13:00:00Z' })])),
    })

    await screen.findByRole('link', { name: /Notifications/ })
    expect(screen.queryByLabelText(/unread notification/)).not.toBeInTheDocument()
  })

  it('drops when an alert is read', async () => {
    renderNotifications({
      [NOTIFICATION_PATHS.inbox]: async () =>
        Response.json(anInbox([aNotification({ id: 'alert-1' })])),
      [NOTIFICATION_PATHS.markRead('alert-1')]: async () => new Response(null, { status: 204 }),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Mark read' }))

    expect(screen.queryByLabelText(/unread notification/)).not.toBeInTheDocument()
  })
})
