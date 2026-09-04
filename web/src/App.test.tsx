import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { CATALOG_PATHS } from '@/lib/catalog'
import { BACKEND_READINESS_PATH } from '@/lib/gateway'
import { reachable, routed, unreachable } from '@/test/backend'
import { stubPrefersDark } from '@/test/match-media'
import { renderApp } from '@/test/render-app'
import { SIGNED_IN_EMAIL, signedInGateway, storeSession } from '@/test/session'

/**
 * The shell wraps every route, and its default route is now the flights list —
 * so a shell test that renders at '/' has to answer the catalog list too, or the
 * page it's incidentally mounting has nothing to render. An empty list keeps
 * these tests about the header, not the catalog.
 */
function shellGateway(): typeof fetch {
  return routed({
    [BACKEND_READINESS_PATH]: (request) => reachable(request.url),
    [CATALOG_PATHS.flights]: async () => Response.json([]),
  })
}

beforeEach(() => {
  localStorage.clear()
  document.documentElement.classList.remove('dark')
  stubPrefersDark(false)
})

afterEach(() => vi.unstubAllGlobals())

describe('App shell', () => {
  it('reports connected once the watched service answers through the gateway', async () => {
    renderApp(shellGateway())

    expect(await screen.findByRole('status')).toHaveTextContent(/connected/i)
  })

  it('reports disconnected when the watched service cannot be reached', async () => {
    renderApp(unreachable)

    expect(await screen.findByRole('status')).toHaveTextContent(/disconnected/i)
  })

  /** The badge measures one service, so it must not claim to speak for the backend. */
  it('names the service it watched rather than the backend as a whole', async () => {
    renderApp(unreachable)

    expect(await screen.findByRole('status')).toHaveAttribute('title', expect.stringMatching(/catalog/i))
  })

  it('toggles the theme from the header', async () => {
    renderApp(shellGateway())

    await userEvent.click(screen.getByRole('button', { name: /switch to dark theme/i }))

    expect(document.documentElement).toHaveClass('dark')
    expect(screen.getByRole('button', { name: /switch to light theme/i })).toBeInTheDocument()
  })

  // The flights list ('/') and a flight's detail ('/flights/:id') are real pages
  // now — covered by their own suites; these are the routes still standing in for
  // later issues plus the auth forms.
  it.each([
    ['/bookings', /bookings/i],
    ['/notifications', /notifications/i],
    ['/login', /sign in/i],
    ['/register', /register/i],
  ])('routes %s to the page later issues fill in', async (route, heading) => {
    renderApp(reachable, route)

    expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument()
  })

  it('shows a not-found page for an unknown route', async () => {
    renderApp(reachable, '/nowhere')

    expect(await screen.findByRole('heading', { name: /not found/i })).toBeInTheDocument()
  })

  /** Browsing stays unauthenticated, so the header invites rather than gates. */
  it('offers the way in while signed out', () => {
    renderApp(shellGateway())

    expect(screen.getByRole('link', { name: /sign in/i })).toHaveAttribute('href', '/login')
    expect(screen.getByRole('link', { name: /register/i })).toHaveAttribute('href', '/register')
  })

  it('names the signed-in User and offers the way out', () => {
    storeSession()

    renderApp(signedInGateway({ [CATALOG_PATHS.flights]: async () => Response.json([]) }))

    expect(screen.getByText(SIGNED_IN_EMAIL)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /sign out/i })).toBeInTheDocument()
  })
})
