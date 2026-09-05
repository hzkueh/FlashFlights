import { Link, useNavigate } from 'react-router'

import { Button } from '@/components/ui/button'
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
    <div className="space-y-3 rounded-lg border bg-card p-5">
      <p className="text-sm">Sign in to see the flights you&apos;ve booked.</p>
      <Button onClick={() => navigate('/login', { state: { from: '/bookings' } })}>Sign in</Button>
    </div>
  )
}

function BookingHistory({ token, userId }: { token: string; userId: string }) {
  // Keyed by the buyer, so switching accounts reloads rather than showing the
  // previous buyer's Bookings.
  const history = useAsync((signal) => loadBookingHistory(token, signal), userId)

  if (history.status === 'loading') {
    return <p className="text-muted-foreground text-sm">Loading your bookings…</p>
  }

  if (history.status === 'error') {
    return (
      <p role="alert" className="text-destructive text-sm">
        Couldn&apos;t load your bookings. Check the gateway is running and try again.
      </p>
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
    <div className="space-y-3 rounded-lg border bg-card p-5">
      <p className="text-sm">You haven&apos;t booked any flights yet.</p>
      <Button asChild variant="outline">
        <Link to="/">Browse flights</Link>
      </Button>
    </div>
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

        <div className="text-right">
          <p className="text-lg font-semibold tabular-nums">{formatPrice(booking.pricePaid)}</p>
          <p className="text-muted-foreground text-xs tabular-nums">
            Booked {formatDateTime(booking.confirmedAt)}
          </p>
        </div>
      </div>
    </div>
  )
}
