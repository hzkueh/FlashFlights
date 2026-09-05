import { Link } from 'react-router'

import { FlashSaving } from '@/components/flash-saving'
import { SaleStateBadge } from '@/components/sale-state-badge'
import { useAsync } from '@/hooks/use-async'
import { useNow } from '@/hooks/use-now'
import { type Flight, listFlights } from '@/lib/catalog'
import { formatDeparture, formatPrice, formatRoute, saleTimeLeft } from '@/lib/format'

/**
 * The catalog list: every flight running a flash sale, live ones first. Reads
 * from Catalog alone — the remaining-seats figure is its advisory SeatCounts, so
 * this page never touches Ordering and never sits on the hot booking path
 * (ADR-0001). Browsable signed out.
 */
export function FlightsPage() {
  const flights = useAsync(listFlights, 'flights')

  return (
    <section className="space-y-6">
      <header className="space-y-1">
        <h1 className="font-heading text-2xl font-semibold tracking-tight">Flights</h1>
        <p className="text-muted-foreground text-sm">
          Flash sales on a fixed pool of seats. Pick a flight to see its live seat map.
        </p>
      </header>

      {flights.status === 'loading' && (
        <p className="text-muted-foreground text-sm">Loading flights…</p>
      )}

      {flights.status === 'error' && (
        <p role="alert" className="text-destructive text-sm">
          Couldn&apos;t load the flights. Check the gateway is running and try again.
        </p>
      )}

      {flights.status === 'ready' &&
        (flights.data.length === 0 ? (
          <p className="text-muted-foreground text-sm">No flash sales are scheduled right now.</p>
        ) : (
          <ul className="grid gap-4 sm:grid-cols-2">
            {flights.data.map((flight) => (
              <li key={flight.id}>
                <FlightCard flight={flight} />
              </li>
            ))}
          </ul>
        ))}
    </section>
  )
}

function FlightCard({ flight }: { flight: Flight }) {
  const now = useNow()

  return (
    <Link
      to={`/flights/${flight.id}`}
      className="block rounded-lg border bg-card p-5 transition-colors hover:border-foreground/30 focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50"
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="font-heading text-lg font-semibold tracking-tight">
            {formatRoute(flight.origin, flight.destination)}
          </p>
          <p className="text-muted-foreground text-xs">
            {flight.flightNumber} · {formatDeparture(flight.departureAt)}
          </p>
        </div>
        <SaleStateBadge state={flight.saleState} />
      </div>

      <div className="mt-4 flex items-end justify-between gap-3">
        <div>
          <div className="flex items-baseline gap-2">
            <p className="text-2xl font-semibold tabular-nums">{formatPrice(flight.flashPrice)}</p>
            <FlashSaving flight={flight} />
          </div>
          <p className="text-muted-foreground text-xs">{remainingSeatsLabel(flight)}</p>
        </div>
        <p className="text-muted-foreground text-sm tabular-nums">
          {saleTimeLeft(flight.saleState, flight.saleStartsAt, flight.saleEndsAt, now)}
        </p>
      </div>
    </Link>
  )
}

/**
 * "9 of 12 seats left", or a dash when the counts are not known yet — never "0
 * left" for unknown, which would read as sold out (CONTEXT.md: the counts are
 * advisory, and their absence is not zero).
 */
function remainingSeatsLabel(flight: Flight): string {
  if (flight.seatCounts === null) {
    return 'Seats — of —'
  }

  return `${flight.seatCounts.available} of ${flight.seatCounts.total} seats left`
}
