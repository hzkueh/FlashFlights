import { Link, useParams } from 'react-router'

import { FlashSaving } from '@/components/flash-saving'
import { SaleStateBadge } from '@/components/sale-state-badge'
import { SeatCheckout } from '@/components/seat-checkout'
import { Button } from '@/components/ui/button'
import { useAsync } from '@/hooks/use-async'
import { useNow } from '@/hooks/use-now'
import { type Flight, getFlight } from '@/lib/catalog'
import { formatDeparture, formatPrice, formatRoute, saleTimeLeft } from '@/lib/format'

/**
 * One flight in detail, with its live seat map. The metadata comes from Catalog
 * and the seat map from Ordering — two reads from two places, on purpose: the
 * map is per-Seat status Catalog does not hold (ADR-0001), read live so a seat
 * freed by an expiry shows Available here before Catalog's counts catch up.
 *
 * Selecting Seats, holding them, and the Hold countdown live in
 * {@link SeatCheckout}; this page loads the flight and hands it down. Browsable
 * signed out — holding is what prompts a sign-in, not looking.
 */
export function FlightPage() {
  const { flightId = '' } = useParams()
  const flight = useAsync((signal) => getFlight(flightId, signal), flightId)

  if (flight.status === 'loading') {
    return <p className="text-muted-foreground text-sm">Loading flight…</p>
  }

  if (flight.status === 'error') {
    return (
      <p role="alert" className="text-destructive text-sm">
        Couldn&apos;t load this flight. Check the gateway is running and try again.
      </p>
    )
  }

  if (flight.data === null) {
    return <FlightNotFound />
  }

  return <FlightDetail flight={flight.data} />
}

function FlightNotFound() {
  return (
    <section className="space-y-4">
      <h1 className="font-heading text-2xl font-semibold tracking-tight">Flight not found</h1>
      <p className="text-muted-foreground text-sm">This flight isn&apos;t in the catalog.</p>
      <Button asChild variant="outline">
        <Link to="/">Back to flights</Link>
      </Button>
    </section>
  )
}

function FlightDetail({ flight }: { flight: Flight }) {
  const now = useNow()

  return (
    <section className="space-y-8">
      <header className="space-y-3">
        <Link to="/" className="text-muted-foreground text-sm hover:text-foreground">
          ← All flights
        </Link>

        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="space-y-1">
            <h1 className="font-heading text-3xl font-semibold tracking-tight">
              {formatRoute(flight.origin, flight.destination)}
            </h1>
            <p className="text-muted-foreground text-sm">
              {flight.flightNumber} · {formatDeparture(flight.departureAt)}
            </p>
          </div>
          <SaleStateBadge state={flight.saleState} />
        </div>

        <div className="flex flex-wrap items-baseline gap-x-6 gap-y-1">
          <div className="flex items-baseline gap-2">
            <p className="text-2xl font-semibold tabular-nums">{formatPrice(flight.flashPrice)}</p>
            <FlashSaving flight={flight} />
          </div>
          <p className="text-muted-foreground text-sm tabular-nums">
            {saleTimeLeft(flight.saleState, flight.saleStartsAt, flight.saleEndsAt, now)}
          </p>
        </div>
      </header>

      <SeatCheckout flight={flight} />
    </section>
  )
}
