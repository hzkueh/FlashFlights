import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { App } from '@/App'
import { ThemeProvider } from '@/hooks/use-theme'
import { stubPrefersDark } from '@/test/matchMedia'

function renderApp(fetchImpl: typeof fetch, route = '/') {
  vi.stubGlobal('fetch', vi.fn(fetchImpl))

  return render(
    <ThemeProvider>
      <MemoryRouter initialEntries={[route]}>
        <App />
      </MemoryRouter>
    </ThemeProvider>,
  )
}

const reachable: typeof fetch = async () =>
  Response.json({ service: 'catalog', status: 'Healthy', checks: {} })

const unreachable: typeof fetch = async () => {
  throw new TypeError('Failed to fetch')
}

beforeEach(() => {
  localStorage.clear()
  document.documentElement.classList.remove('dark')
  stubPrefersDark(false)
})

afterEach(() => vi.unstubAllGlobals())

describe('App shell', () => {
  it('reports the backend as connected once it answers through the gateway', async () => {
    renderApp(reachable)

    expect(await screen.findByRole('status')).toHaveTextContent(/connected/i)
  })

  it('reports the backend as disconnected when it cannot be reached', async () => {
    renderApp(unreachable)

    expect(await screen.findByRole('status')).toHaveTextContent(/disconnected/i)
  })

  it('toggles the theme from the header', async () => {
    renderApp(reachable)

    await userEvent.click(screen.getByRole('button', { name: /switch to dark theme/i }))

    expect(document.documentElement).toHaveClass('dark')
    expect(screen.getByRole('button', { name: /switch to light theme/i })).toBeInTheDocument()
  })

  it('routes to the pages later tickets fill in', async () => {
    renderApp(reachable, '/bookings')

    expect(await screen.findByRole('heading', { name: /bookings/i })).toBeInTheDocument()
  })

  it('shows a not-found page for an unknown route', async () => {
    renderApp(reachable, '/nowhere')

    expect(await screen.findByRole('heading', { name: /not found/i })).toBeInTheDocument()
  })
})
