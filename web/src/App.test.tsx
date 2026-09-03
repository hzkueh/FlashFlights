import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { App } from '@/App'
import { ThemeProvider } from '@/hooks/use-theme'
import { reachable, stubFetch, unreachable } from '@/test/backend'
import { stubPrefersDark } from '@/test/match-media'

function renderApp(fetchImpl: typeof fetch, route = '/') {
  stubFetch(fetchImpl)

  return render(
    <ThemeProvider>
      <MemoryRouter initialEntries={[route]}>
        <App />
      </MemoryRouter>
    </ThemeProvider>,
  )
}

beforeEach(() => {
  localStorage.clear()
  document.documentElement.classList.remove('dark')
  stubPrefersDark(false)
})

afterEach(() => vi.unstubAllGlobals())

describe('App shell', () => {
  it('reports connected once the watched service answers through the gateway', async () => {
    renderApp(reachable)

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
    renderApp(reachable)

    await userEvent.click(screen.getByRole('button', { name: /switch to dark theme/i }))

    expect(document.documentElement).toHaveClass('dark')
    expect(screen.getByRole('button', { name: /switch to light theme/i })).toBeInTheDocument()
  })

  it.each([
    ['/bookings', /bookings/i],
    ['/notifications', /notifications/i],
    ['/login', /sign in/i],
    ['/register', /register/i],
    ['/flights/abc', /flight/i],
  ])('routes %s to the page later issues fill in', async (route, heading) => {
    renderApp(reachable, route)

    expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument()
  })

  it('shows a not-found page for an unknown route', async () => {
    renderApp(reachable, '/nowhere')

    expect(await screen.findByRole('heading', { name: /not found/i })).toBeInTheDocument()
  })
})
