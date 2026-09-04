import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { SessionProvider, useSession } from '@/hooks/use-session'
import { AUTH_PATHS } from '@/lib/auth'
import { routed, stubFetch, unreachable } from '@/test/backend'
import { SIGNED_IN_EMAIL, storeSession, storedSession } from '@/test/session'

/** The only two things a caller of the provider can see: who is signed in, and the way out. */
function Probe() {
  const { session, signOut } = useSession()

  return (
    <>
      <span data-testid="who">{session?.email ?? 'signed out'}</span>
      <button type="button" onClick={signOut}>
        Sign out
      </button>
    </>
  )
}

function renderProbe(fetchImpl: typeof fetch) {
  stubFetch(fetchImpl)

  return render(
    <SessionProvider>
      <Probe />
    </SessionProvider>,
  )
}

beforeEach(() => localStorage.clear())
afterEach(() => vi.unstubAllGlobals())

describe('staying signed in', () => {
  /**
   * Restored before the gateway is asked anything, so a reload does not flash a
   * signed-out header at a User who is signed in.
   */
  it('reports the stored session on the first render, without waiting for the gateway', () => {
    storeSession()

    renderProbe(routed({}))

    expect(screen.getByTestId('who')).toHaveTextContent(SIGNED_IN_EMAIL)
  })

  it('has no session when nothing is stored', () => {
    renderProbe(routed({}))

    expect(screen.getByTestId('who')).toHaveTextContent('signed out')
  })

  /**
   * A stored token can have been signed with a key the system no longer uses,
   * and only the gateway knows that — so the restored session is checked behind
   * the first render.
   */
  it('drops a stored session the gateway no longer accepts', async () => {
    storeSession()

    renderProbe(routed({ [AUTH_PATHS.me]: async () => new Response('', { status: 401 }) }))

    await waitFor(() => expect(screen.getByTestId('who')).toHaveTextContent('signed out'))
    expect(storedSession()).toBeNull()
  })

  /**
   * The gateway being down says nothing about whether the token is good, and
   * signing the User out for it would throw away a session that still works.
   */
  it('keeps the session when the gateway cannot be reached at all', async () => {
    storeSession()
    const gatewayIsDown = vi.fn(unreachable)

    renderProbe(gatewayIsDown)

    await waitFor(() => expect(gatewayIsDown).toHaveBeenCalled())
    expect(screen.getByTestId('who')).toHaveTextContent(SIGNED_IN_EMAIL)
    expect(storedSession()).not.toBeNull()
  })

  it('forgets the session on sign out, so the next visit starts signed out', async () => {
    storeSession()
    renderProbe(
      routed({ [AUTH_PATHS.me]: async () => Response.json({ userId: 'u', email: SIGNED_IN_EMAIL }) }),
    )

    await userEvent.click(screen.getByRole('button', { name: /sign out/i }))

    expect(screen.getByTestId('who')).toHaveTextContent('signed out')
    expect(storedSession()).toBeNull()
  })
})
