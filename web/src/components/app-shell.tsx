import { NavLink, Outlet } from 'react-router'

import { BackendStatusBadge } from '@/components/backend-status-badge'
import { SessionMenu } from '@/components/session-menu'
import { ThemeToggle } from '@/components/theme-toggle'
import { useNotifications } from '@/hooks/use-notifications'
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
  const { unreadCount } = useNotifications()

  return (
    <div className="flex min-h-dvh flex-col">
      <header className="sticky top-0 z-10 border-b bg-background/80 backdrop-blur">
        {/* Wraps onto a second line rather than overflowing or squashing: at phone
            widths the brand and controls take the first row and the nav the
            second, so nothing is pushed off the edge and nothing is hidden. */}
        <div className="mx-auto flex w-full max-w-5xl flex-wrap items-center gap-x-4 gap-y-2 px-4 py-2 sm:h-14 sm:flex-nowrap sm:gap-x-6 sm:py-0">
          <NavLink to="/" className="font-heading text-base font-semibold tracking-tight">
            FlashFlights
          </NavLink>

          <nav className="order-last flex w-full items-center gap-4 text-sm sm:order-none sm:w-auto">
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
                {item.to === '/notifications' && unreadCount > 0 && (
                  <span
                    // The count, not a dot: "3 unread" is worth knowing before
                    // opening the page, and it is the same number the page shows
                    // because both read it from the same place.
                    aria-label={`${unreadCount} unread ${unreadCount === 1 ? 'notification' : 'notifications'}`}
                    className="ml-1.5 inline-flex h-4 min-w-4 items-center justify-center rounded-full bg-primary px-1 text-[0.65rem] font-medium text-primary-foreground tabular-nums"
                  >
                    {unreadCount}
                  </span>
                )}
              </NavLink>
            ))}
          </nav>

          <div className="ml-auto flex items-center gap-1 sm:gap-3">
            <SessionMenu />
            <BackendStatusBadge />
            <ThemeToggle />
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-5xl flex-1 px-4 py-6 sm:py-8">
        <Outlet />
      </main>
    </div>
  )
}
