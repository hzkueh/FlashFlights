import { motion } from 'motion/react'
import { Link } from 'react-router'

import { SaleCountdown } from '@/components/countdown'
import { FlashSaving } from '@/components/flash-saving'
import { SaleStateBadge } from '@/components/sale-state-badge'
import { GatewayErrorPanel, StatePanel } from '@/components/state-panel'
import { LoadingPanel } from '@/components/loading-panel'
import { Skeleton } from '@/components/ui/skeleton'
import { useAsync } from '@/hooks/use-async'
import { useReducedMotion } from '@/hooks/use-reduced-motion'
import { type Flight, listFlights } from '@/lib/catalog'
import { formatDeparture, formatPrice, formatRoute } from '@/lib/format'

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

      {flights.status === 'loading' && <FlightListSkeleton />}

      {flights.status === 'error' && (
        <GatewayErrorPanel what="the flights" />
      )}

      {flights.status === 'ready' &&
        (flights.data.length === 0 ? (
          <StatePanel title="No flash sales are scheduled right now.">
            <p className="text-muted-foreground text-sm">
              Check back soon — sales open and close on their own schedule.
            </p>
          </StatePanel>
        ) : (
          <ul className="grid gap-4 sm:grid-cols-2">
            {flights.data.map((flight, index) => (
              <li key={flight.id}>
                <FlightCard flight={flight} index={index} />
              </li>
            ))}
          </ul>
        ))}
    </section>
  )
}

function FlightCard({ flight, index }: { flight: Flight; index: number }) {
  const reducedMotion = useReducedMotion()

  return (
    <motion.div
      // The list settles in rather than appearing all at once, each card a beat
      // behind the one before it. Capped, so a long catalog does not make the last
      // card wait — and skipped entirely for a reader who asked for less motion.
      initial={reducedMotion ? false : { opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.25, delay: Math.min(index, 5) * 0.04, ease: 'easeOut' }}
      className="h-full"
    >
      <Link
        to={`/flights/${flight.id}`}
        className="block h-full rounded-lg border bg-card p-5 transition-colors hover:border-primary/50 focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50"
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

        <div className="mt-4 flex flex-wrap items-end justify-between gap-x-3 gap-y-2">
          <div>
            <div className="flex items-baseline gap-2">
              <p className="text-2xl font-semibold tabular-nums">{formatPrice(flight.flashPrice)}</p>
              <FlashSaving flight={flight} />
            </div>
            <p className="text-muted-foreground text-xs">{remainingSeatsLabel(flight)}</p>
          </div>
          <SaleCountdown flight={flight} />
        </div>
      </Link>
    </motion.div>
  )
}

/**
 * The shape of the list while Catalog is answering. Four cards, because the
 * catalog is small by design (spec) and a screenful of placeholder for a
 * two-flight catalog would be a lie about what is coming.
 */
function FlightListSkeleton() {
  return (
    <LoadingPanel label="Loading flights…" className="grid gap-4 sm:grid-cols-2">
      {[0, 1, 2, 3].map((card) => (
        <div key={card} className="space-y-4 rounded-lg border bg-card p-5">
          <div className="flex items-start justify-between gap-3">
            <div className="space-y-2">
              <Skeleton className="h-5 w-40" />
              <Skeleton className="h-3 w-28" />
            </div>
            <Skeleton className="h-5 w-20 rounded-full" />
          </div>
          <div className="flex items-end justify-between gap-3">
            <div className="space-y-2">
              <Skeleton className="h-7 w-24" />
              <Skeleton className="h-3 w-32" />
            </div>
            <Skeleton className="h-4 w-20" />
          </div>
        </div>
      ))}
    </LoadingPanel>
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
