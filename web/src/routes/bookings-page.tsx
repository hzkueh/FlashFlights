import { Link, useNavigate } from 'react-router'

import { GatewayErrorPanel, StatePanel } from '@/components/state-panel'
import { Button } from '@/components/ui/button'
import { LoadingPanel } from '@/components/loading-panel'
import { Skeleton } from '@/components/ui/skeleton'
import { useAsync } from '@/hooks/use-async'
import { useSession } from '@/hooks/use-session'
import { type BookingWithFlight, loadBookingHistory } from '@/lib/bookings'
import { formatDateTime, formatPrice, formatRoute } from '@/lib/format'

/**
 * A buyer's past Bookings. Signed-in only — a Booking belongs to one buyer, and
 * the list is scoped by the token — so a signed-out visitor is prompted to sign
 * in rather than shown an empty page.
 *
 * The reads are two, from two places (the same split the detail page uses): the
 * Bookings and their Seats come from Ordering, the flight each is for from
 * Catalog. That join lives in {@link loadBookingHistory}; this page renders its
 * four states.
 */
export function BookingsPage() {
  const { session } = useSession()

  return (
    <section className="space-y-6">
      <header className="space-y-1">
        <h1 className="font-heading text-2xl font-semibold tracking-tight">Bookings</h1>
        <p className="text-muted-foreground text-sm">Your confirmed flash-sale purchases.</p>
      </header>

      {session === null ? <SignedOut /> : <BookingHistory token={session.token} userId={session.userId} />}
    </section>
  )
}

function SignedOut() {
  const navigate = useNavigate()

  return (
    <StatePanel title="Sign in to see the flights you've booked.">
      <Button onClick={() => navigate('/login', { state: { from: '/bookings' } })}>Sign in</Button>
    </StatePanel>
  )
}

function BookingHistory({ token, userId }: { token: string; userId: string }) {
  // Keyed by the buyer, so switching accounts reloads rather than showing the
  // previous buyer's Bookings.
  const history = useAsync((signal) => loadBookingHistory(token, signal), userId)

  if (history.status === 'loading') {
    return <BookingHistorySkeleton />
  }

  if (history.status === 'error') {
    return (
      <GatewayErrorPanel what="your bookings" />
    )
  }

  if (history.data.length === 0) {
    return <EmptyHistory />
  }

  return (
    <ul className="space-y-4">
      {history.data.map((entry) => (
        <li key={entry.booking.bookingId}>
          <BookingCard entry={entry} />
        </li>
      ))}
    </ul>
  )
}

function EmptyHistory() {
  return (
    <StatePanel title="You haven't booked any flights yet.">
      <p className="text-muted-foreground text-sm">
        Flash sales run on a fixed pool of seats — pick one before its window closes.
      </p>
      <Button asChild variant="outline">
        <Link to="/">Browse flights</Link>
      </Button>
    </StatePanel>
  )
}

/** Two bookings' worth of shape while Ordering and Catalog are both answering. */
function BookingHistorySkeleton() {
  return (
    <LoadingPanel label="Loading your bookings…" className="space-y-4">
      {[0, 1].map((card) => (
        <div key={card} className="flex flex-wrap items-start justify-between gap-3 rounded-lg border bg-card p-5">
          <div className="space-y-2">
            <Skeleton className="h-5 w-40" />
            <Skeleton className="h-3 w-20" />
            <Skeleton className="h-4 w-28" />
          </div>
          <div className="flex flex-col items-end gap-2">
            <Skeleton className="h-6 w-20" />
            <Skeleton className="h-3 w-32" />
          </div>
        </div>
      ))}
    </LoadingPanel>
  )
}

function BookingCard({ entry }: { entry: BookingWithFlight }) {
  const { booking, flight } = entry
  const seatList = booking.seats.map((seat) => seat.seatNumber).join(', ')

  return (
    <div className="rounded-lg border bg-card p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          {flight === null ? (
            // A Flight Catalog no longer lists — the Booking still stands, so it is
            // shown without a route rather than hidden.
            <p className="font-heading text-lg font-semibold tracking-tight">Flight no longer listed</p>
          ) : (
            <>
              <p className="font-heading text-lg font-semibold tracking-tight">
                {formatRoute(flight.origin, flight.destination)}
              </p>
              <p className="text-muted-foreground text-xs">{flight.flightNumber}</p>
            </>
          )}
          <p className="text-muted-foreground text-sm tabular-nums">Seats {seatList}</p>
        </div>

        <div className="sm:text-right">
          <p className="text-lg font-semibold tabular-nums">{formatPrice(booking.pricePaid)}</p>
          <p className="text-muted-foreground text-xs tabular-nums">
            Booked {formatDateTime(booking.confirmedAt)}
          </p>
        </div>
      </div>
    </div>
  )
}
