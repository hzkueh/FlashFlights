import { Link, useParams } from 'react-router'

import { SaleCountdown } from '@/components/countdown'
import { FlashSaving } from '@/components/flash-saving'
import { SaleStateBadge } from '@/components/sale-state-badge'
import { SeatCheckout } from '@/components/seat-checkout'
import { StatePanel } from '@/components/state-panel'
import { Button } from '@/components/ui/button'
import { LoadingPanel, Skeleton } from '@/components/ui/skeleton'
import { WatchToggle } from '@/components/watch-toggle'
import { useAsync } from '@/hooks/use-async'
import { type Flight, getFlight } from '@/lib/catalog'
import { formatDeparture, formatPrice, formatRoute } from '@/lib/format'

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
    return <FlightDetailSkeleton />
  }

  if (flight.status === 'error') {
    return (
      <StatePanel tone="error" title="Couldn't load this flight.">
        <p className="text-muted-foreground text-sm">
          Check the gateway is running, then try again.
        </p>
        <Button asChild variant="outline">
          <Link to="/">Back to flights</Link>
        </Button>
      </StatePanel>
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
      <StatePanel title="This flight isn't in the catalog.">
        <p className="text-muted-foreground text-sm">
          It may have been withdrawn since you last saw it.
        </p>
        <Button asChild variant="outline">
          <Link to="/">Back to flights</Link>
        </Button>
      </StatePanel>
    </section>
  )
}

/**
 * The detail page's own shape while Catalog answers — header, price row, and the
 * block the seat map will fill. {@link SeatCheckout} draws its own cabin-shaped
 * placeholder once this resolves; this one only has to stop the page jumping.
 */
function FlightDetailSkeleton() {
  return (
    <LoadingPanel label="Loading flight…" className="space-y-8">
      <div className="space-y-3">
        <Skeleton className="h-4 w-24" />
        <Skeleton className="h-9 w-64 max-w-full" />
        <Skeleton className="h-4 w-48" />
        <div className="flex flex-wrap items-center gap-x-6 gap-y-2 pt-1">
          <Skeleton className="h-8 w-28" />
          <Skeleton className="h-4 w-24" />
        </div>
      </div>
      <Skeleton className="h-56 w-full max-w-sm" />
    </LoadingPanel>
  )
}

function FlightDetail({ flight }: { flight: Flight }) {
  return (
    <section className="space-y-8">
      <header className="space-y-3">
        <Link to="/" className="text-muted-foreground text-sm hover:text-foreground">
          ← All flights
        </Link>

        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="space-y-1">
            <h1 className="font-heading text-2xl font-semibold tracking-tight sm:text-3xl">
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
          <SaleCountdown flight={flight} />
        </div>
      </header>

      <WatchToggle flight={flight} />

      <SeatCheckout flight={flight} />
    </section>
  )
}
