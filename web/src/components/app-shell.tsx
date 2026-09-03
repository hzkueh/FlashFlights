import { NavLink, Outlet } from 'react-router'

import { BackendStatusBadge } from '@/components/backend-status-badge'
import { SessionMenu } from '@/components/session-menu'
import { ThemeToggle } from '@/components/theme-toggle'
import { cn } from '@/lib/utils'

/** The screens from the spec, in the order a buyer meets them. */
const NAV = [
  // `end` only matters for '/', which would otherwise match every route — but
  // every entry carries it so the type stays uniform.
  { to: '/', label: 'Flights', end: true },
  { to: '/bookings', label: 'Bookings', end: false },
  { to: '/notifications', label: 'Notifications', end: false },
] as const

/**
 * Header, navigation, and the page frame every route renders into. Deliberately
 * thin — later tickets add screens under it, not chrome around it.
 */
export function AppShell() {
  return (
    <div className="flex min-h-dvh flex-col">
      <header className="sticky top-0 z-10 border-b bg-background/80 backdrop-blur">
        <div className="mx-auto flex h-14 w-full max-w-5xl items-center gap-6 px-4">
          <NavLink to="/" className="font-heading text-base font-semibold tracking-tight">
            FlashFlights
          </NavLink>

          <nav className="flex items-center gap-4 text-sm">
            {NAV.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                className={({ isActive }) =>
                  cn(
                    'text-muted-foreground transition-colors hover:text-foreground',
                    isActive && 'text-foreground font-medium',
                  )
                }
              >
                {item.label}
              </NavLink>
            ))}
          </nav>

          <div className="ml-auto flex items-center gap-3">
            <SessionMenu />
            <BackendStatusBadge />
            <ThemeToggle />
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-5xl flex-1 px-4 py-8">
        <Outlet />
      </main>
    </div>
  )
}
