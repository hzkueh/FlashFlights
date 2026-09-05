import { Link, useParams } from 'react-router'

import { FlashSaving } from '@/components/flash-saving'
import { SaleStateBadge } from '@/components/sale-state-badge'
import { Button } from '@/components/ui/button'
import { useAsync } from '@/hooks/use-async'
import { useNow } from '@/hooks/use-now'
import { type Flight, getFlight } from '@/lib/catalog'
import { formatDeparture, formatPrice, formatRoute, saleTimeLeft } from '@/lib/format'
import { type Seat, type SeatStatus, getSeatMap } from '@/lib/seat-map'

/**
 * One flight in detail, with its live seat map. The metadata comes from Catalog
 * and the seat map from Ordering — two reads from two places, on purpose: the
 * map is per-Seat status Catalog does not hold (ADR-0001), read live so a seat
 * freed by an expiry shows Available here before Catalog's counts catch up.
 *
 * Selecting seats and holding them is ticket 07 — this page only shows the map.
 * Browsable signed out.
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

      <SeatMapPanel flightId={flight.id} />
    </section>
  )
}

function SeatMapPanel({ flightId }: { flightId: string }) {
  const map = useAsync((signal) => getSeatMap(flightId, signal), flightId)

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="font-heading text-lg font-semibold tracking-tight">Seat map</h2>
        <SeatLegend />
      </div>

      {map.status === 'loading' && <p className="text-muted-foreground text-sm">Loading seats…</p>}

      {map.status === 'error' && (
        <p role="alert" className="text-destructive text-sm">
          Couldn&apos;t load the seat map. It updates live once Ordering is reachable.
        </p>
      )}

      {map.status === 'ready' &&
        (map.data.seats.length === 0 ? (
          <p className="text-muted-foreground text-sm">This flight has no seats yet.</p>
        ) : (
          <SeatGrid seats={map.data.seats} />
        ))}
    </div>
  )
}

function SeatGrid({ seats }: { seats: Seat[] }) {
  const rows = groupByRow(seats)

  return (
    <div className="w-fit space-y-2 rounded-lg border bg-card p-4">
      {rows.map(([rowNumber, rowSeats]) => (
        <div key={rowNumber} className="flex items-center gap-2">
          <span className="text-muted-foreground w-6 text-right text-xs tabular-nums">{rowNumber}</span>
          {rowSeats.map((seat) => (
            <SeatCell key={seat.seatId} seat={seat} />
          ))}
        </div>
      ))}
    </div>
  )
}

const STATUS_CELL: Record<SeatStatus, string> = {
  Available: 'border-border bg-background text-foreground',
  Held: 'border-transparent bg-secondary text-secondary-foreground',
  Confirmed: 'border-transparent bg-muted text-muted-foreground line-through',
}

function SeatCell({ seat }: { seat: Seat }) {
  return (
    <span
      data-status={seat.status}
      aria-label={`Seat ${seat.seatNumber}, ${seat.status}`}
      title={`${seat.seatNumber} — ${seat.status}`}
      className={`inline-flex h-9 w-9 items-center justify-center rounded-md border text-xs font-medium tabular-nums ${STATUS_CELL[seat.status]}`}
    >
      {seat.column}
    </span>
  )
}

const LEGEND: SeatStatus[] = ['Available', 'Held', 'Confirmed']

function SeatLegend() {
  return (
    <ul className="flex flex-wrap items-center gap-3">
      {LEGEND.map((status) => (
        <li key={status} className="flex items-center gap-1.5">
          <span className={`h-3.5 w-3.5 rounded-sm border ${STATUS_CELL[status]}`} aria-hidden="true" />
          <span className="text-muted-foreground text-xs">{status}</span>
        </li>
      ))}
    </ul>
  )
}

/** Seats arrive in grid order already; this only splits the flat list into rows. */
function groupByRow(seats: Seat[]): [number, Seat[]][] {
  const rows = new Map<number, Seat[]>()

  for (const seat of seats) {
    const row = rows.get(seat.row) ?? []
    row.push(seat)
    rows.set(seat.row, row)
  }

  return [...rows.entries()]
}
