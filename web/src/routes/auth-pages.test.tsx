import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { AUTH_PATHS } from '@/lib/auth'
import type { Route } from '@/test/backend'
import { stubPrefersDark } from '@/test/match-media'
import { renderApp } from '@/test/render-app'
import { aSession, refusal, signedInGateway, storedSession, validationProblem } from '@/test/session'

const PASSWORD = 'Flash-Sale-2026'

/**
 * The auth screens through the whole app rather than in isolation: what a buyer
 * is actually promised is that signing in lands them back among the flights
 * with the header saying who they are, and only the app can show that.
 */
function renderAt(route: string, gateway: Record<string, Route> = {}) {
  return renderApp(signedInGateway(gateway), route)
}

async function submit(email: string, label: RegExp) {
  await userEvent.type(screen.getByLabelText(/email/i), email)
  await userEvent.type(screen.getByLabelText(/password/i), PASSWORD)
  await userEvent.click(screen.getByRole('button', { name: label }))
}

beforeEach(() => {
  localStorage.clear()
  stubPrefersDark(false)
})

afterEach(() => vi.unstubAllGlobals())

describe('signing in', () => {
  it('lands the User back among the flights, signed in and remembered', async () => {
    renderAt('/login', { [AUTH_PATHS.login]: async () => Response.json(aSession()) })

    await submit('buyer@flashflights.test', /^sign in$/i)

    expect(await screen.findByRole('heading', { name: /flights/i })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: /sign out/i })).toBeInTheDocument()
    expect(storedSession()?.email).toBe('buyer@flashflights.test')
  })

  it('keeps the User on the form and says what the gateway said', async () => {
    renderAt('/login', {
      [AUTH_PATHS.login]: async () => refusal('That email and password do not match.'),
    })

    await submit('buyer@flashflights.test', /^sign in$/i)

    expect(await screen.findByRole('alert')).toHaveTextContent(/do not match/i)
    expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
    expect(storedSession()).toBeNull()
  })

  /** Being unable to reach the gateway is not the same as being refused, and must not read as it. */
  it('says the gateway could not be reached rather than blaming the credentials', async () => {
    renderAt('/login', {
      [AUTH_PATHS.login]: () => {
        throw new TypeError('Failed to fetch')
      },
    })

    await submit('buyer@flashflights.test', /^sign in$/i)

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be reached/i)
  })
})

describe('registering', () => {
  it('signs the new User straight in rather than asking them to sign in again', async () => {
    renderAt('/register', { [AUTH_PATHS.register]: async () => Response.json(aSession()) })

    await submit('buyer@flashflights.test', /^register$/i)

    expect(await screen.findByRole('heading', { name: /flights/i })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: /sign out/i })).toBeInTheDocument()
  })

  /** The gateway blames a field; the form has to show the message next to it. */
  it('shows a refused password against the password field', async () => {
    renderAt('/register', {
      [AUTH_PATHS.register]: async () =>
        validationProblem({ password: ['Passwords must be at least 8 characters.'] }),
    })

    await submit('buyer@flashflights.test', /^register$/i)

    expect(await screen.findByText(/at least 8 characters/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/password/i)).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByLabelText(/email/i)).not.toHaveAttribute('aria-invalid', 'true')
  })

  it('shows an already-registered email against the email field', async () => {
    renderAt('/register', {
      [AUTH_PATHS.register]: async () =>
        validationProblem({ email: ['That email is already registered.'] }),
    })

    await submit('taken@flashflights.test', /^register$/i)

    expect(await screen.findByText('That email is already registered.')).toBeInTheDocument()
    expect(screen.getByLabelText(/email/i)).toHaveAttribute('aria-invalid', 'true')
  })

  /** Scoped to the page: the header offers its own way in while signed out. */
  it('offers the way to the other form, so neither screen is a dead end', () => {
    renderAt('/register')

    expect(within(screen.getByRole('main')).getByRole('link', { name: /sign in/i }))
      .toHaveAttribute('href', '/login')
  })
})
