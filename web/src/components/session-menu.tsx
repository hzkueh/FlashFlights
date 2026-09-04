import { NavLink } from 'react-router'

import { Button } from '@/components/ui/button'
import { useSession } from '@/hooks/use-session'

/**
 * Who is signed in, and the way in or out. Browsing stays unauthenticated, so
 * this is an invitation in the header rather than a gate in front of the app.
 */
export function SessionMenu() {
  const { session, signOut } = useSession()

  if (session === null) {
    return (
      <div className="flex items-center gap-3 text-sm">
        <NavLink to="/login" className="text-muted-foreground transition-colors hover:text-foreground">
          Sign in
        </NavLink>
        <NavLink to="/register" className="text-muted-foreground transition-colors hover:text-foreground">
          Register
        </NavLink>
      </div>
    )
  }

  return (
    <div className="flex items-center gap-2">
      {/* Hidden on the narrowest screens, where the buttons matter more than
          the label — the email is still on the Sign out button's title. */}
      <span className="text-muted-foreground hidden max-w-40 truncate text-sm sm:inline">
        {session.email}
      </span>
      <Button variant="ghost" size="sm" onClick={signOut} title={`Signed in as ${session.email}`}>
        Sign out
      </Button>
    </div>
  )
}
