import { createContext, use, useEffect, useMemo, useState } from 'react'

import {
  AuthError,
  type Credentials,
  fetchSignedInUser,
  login,
  register,
} from '@/lib/auth'
import { SESSION_STORAGE_KEY, type Session, resolveStoredSession } from '@/lib/session'

interface SessionContextValue {
  /** The signed-in User, or null. */
  session: Session | null
  signIn: (credentials: Credentials) => Promise<void>
  signUp: (credentials: Credentials) => Promise<void>
  signOut: () => void
}

const SessionContext = createContext<SessionContextValue | null>(null)

/**
 * Owns being signed in: restored from storage on load, written back on every
 * change, and re-checked against the gateway.
 *
 * Restored optimistically rather than after the check, so a reload does not
 * flash a signed-out header at a User who is signed in. The check then runs
 * behind it, because a stored token can have been issued under a signing key
 * the system no longer uses, and only the gateway knows that.
 */
export function SessionProvider({ children }: { children: React.ReactNode }) {
  const [session, setSession] = useState<Session | null>(() =>
    resolveStoredSession(localStorage.getItem(SESSION_STORAGE_KEY), new Date()),
  )

  useEffect(() => {
    if (session === null) {
      localStorage.removeItem(SESSION_STORAGE_KEY)

      return
    }

    localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session))
  }, [session])

  const token = session?.token ?? null

  useEffect(() => {
    if (token === null) {
      return
    }

    const controller = new AbortController()

    void (async () => {
      try {
        await fetchSignedInUser(token, controller.signal)
      } catch (error) {
        // Only a refusal means the User is no longer signed in. An unreachable
        // gateway means the gateway is down, and signing them out for that
        // would lose a session that is still perfectly good.
        if (error instanceof AuthError && error.status === 401) {
          setSession(null)
        }
      }
    })()

    return () => controller.abort()
  }, [token])

  const value = useMemo<SessionContextValue>(
    () => ({
      session,
      signIn: async (credentials) => setSession(await login(credentials)),
      signUp: async (credentials) => setSession(await register(credentials)),
      signOut: () => setSession(null),
    }),
    [session],
  )

  return <SessionContext value={value}>{children}</SessionContext>
}

/** Throws outside a {@link SessionProvider} rather than silently reporting signed-out. */
export function useSession(): SessionContextValue {
  const value = use(SessionContext)

  if (value === null) {
    throw new Error('useSession must be used within a SessionProvider')
  }

  return value
}
