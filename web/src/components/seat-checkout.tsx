import { useEffect, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router'

import { Button } from '@/components/ui/button'
import { useAsync } from '@/hooks/use-async'
import { useLiveSeatMap } from '@/hooks/use-live-seat-map'
import { useNow } from '@/hooks/use-now'
import { useSession } from '@/hooks/use-session'
import { type Booking, type ConfirmHoldOutcome, confirmHold } from '@/lib/bookings'
import type { Flight } from '@/lib/catalog'
import { formatDateTime, formatPrice, formatRoute, humanizeDuration } from '@/lib/format'
import {
  type ConflictingSeat,
  type CreateHoldOutcome,
  type Hold,
  createHold,
} from '@/lib/holds'
import { type Seat, type SeatStatus, getSeatMap } from '@/lib/seat-map'

/**
 * The seat map turned interactive: a signed-in buyer selects Available Seats,
 * requests a Hold on exactly those, and then watches it count down. The map is
 * seeded from Ordering's one-off HTTP read (ADR-0001) and then kept live over
 * SignalR (ticket 08), so a Seat another buyer holds, confirms, or lets expire
 * repaints here without a refetch; this buyer's own Hold is reflected from the
 * granted Hold itself — its Seats render as "held by you".
 */
export function SeatCheckout({ flight }: { flight: Flight }) {
  const map = useAsync((signal) => getSeatMap(flight.id, signal), flight.id)

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
          <LiveCheckout flight={flight} initialSeats={map.data.seats} />
        ))}
    </div>
  )
}

/**
 * Keeps the checkout's map live: seeds from the one-off HTTP read, then folds in
 * other buyers' holds, confirms, and expiries as Notifications relays them over
 * SignalR (ticket 08), so the grid a buyer selects from repaints without a
 * refetch that would unmount the countdown. Live updates are advisory — a failed
 * connection falls back to the seeded map, and Ordering's locked grant stays the
 * sole authority (ADR-0001).
 */
function LiveCheckout({ flight, initialSeats }: { flight: Flight; initialSeats: Seat[] }) {
  const seats = useLiveSeatMap(flight.id, initialSeats)

  return <Checkout flight={flight} seats={seats} />
}

/**
 * The buyer's checkout walks these phases: picking Seats, waiting on a Hold,
 * holding one (with a live countdown), paying for it, and finally booked. `held`
 * and `confirming` both show the held Seats and countdown — `confirming` only
 * disables the pay button while the confirm is in flight — so the countdown never
 * unmounts between clicking pay and the Booking coming back.
 */
type Phase =
  | { name: 'selecting' }
  | { name: 'holding' }
  | { name: 'held'; hold: Hold }
  | { name: 'confirming'; hold: Hold }
  | { name: 'booked'; booking: Booking }

/** A message layered over selection: a lost race, a plain failure, or a lapsed Hold. */
type Notice =
  | { kind: 'conflict'; seats: ConflictingSeat[] }
  | { kind: 'expired' }
  | { kind: 'error'; message: string }

function Checkout({ flight, seats }: { flight: Flight; seats: Seat[] }) {
  const { session } = useSession()
  const [phase, setPhase] = useState<Phase>({ name: 'selecting' })
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set())
  const [notice, setNotice] = useState<Notice | null>(null)
  // What a lost race told us about Seats others took, laid over the live map so a
  // just-taken Seat renders as taken (not a stale Available button) the instant
  // the 409 comes back — ahead of, and belt-and-braces with, the SignalR push
  // that will report the same change.
  const [taken, setTaken] = useState<ReadonlyMap<string, SeatStatus>>(new Map())

  const shownSeats = taken.size === 0 ? seats : applyStatuses(seats, taken)
  // The Seats this buyer holds (or has just booked) light up as "held by you" —
  // the map cannot say who holds a Seat, but the checkout knows its own.
  const yourSeatIds = ownSeatIds(phase)

  function toggle(seatId: string) {
    setNotice(null)
    setSelected((current) => {
      const next = new Set(current)

      if (next.has(seatId)) {
        next.delete(seatId)
      } else {
        next.add(seatId)
      }

      return next
    })
  }

  async function hold() {
    if (session === null) {
      return
    }

    setPhase({ name: 'holding' })
    setNotice(null)

    let outcome: CreateHoldOutcome
    try {
      outcome = await createHold(
        { flightId: flight.id, seatIds: [...selected], pricePerSeat: flight.flashPrice },
        session.token,
      )
    } catch {
      // An abort is the only thing createHold throws, and this flow never aborts.
      setPhase({ name: 'selecting' })
      return
    }

    switch (outcome.status) {
      case 'granted':
        // The held Seats render as "held by you" from the Hold itself, so the map
        // needs no re-read — live cross-client refresh is ticket 08's job.
        setSelected(new Set())
        setPhase({ name: 'held', hold: outcome.hold })
        return
      case 'conflict':
        // Drop the Seats that were taken so what stays selected is holdable, and
        // mark them taken so they stop looking selectable; the notice names them,
        // and the buyer can hold the rest or pick others.
        setSelected((current) => without(current, outcome.seats))
        setTaken((current) => withStatuses(current, outcome.seats))
        setNotice({ kind: 'conflict', seats: outcome.seats })
        setPhase({ name: 'selecting' })
        return
      default:
        setNotice({ kind: 'error', message: outcome.message })
        setPhase({ name: 'selecting' })
    }
  }

  /** Takes the Hold through the simulated payment and into a Booking. */
  async function confirm() {
    if (phase.name !== 'held' || session === null) {
      return
    }

    const { hold } = phase
    setPhase({ name: 'confirming', hold })
    setNotice(null)

    let outcome: ConfirmHoldOutcome
    try {
      outcome = await confirmHold(hold.holdId, session.token)
    } catch {
      // An abort is the only thing confirmHold throws, and this flow never aborts.
      setPhase({ name: 'held', hold })
      return
    }

    switch (outcome.status) {
      case 'confirmed':
        setPhase({ name: 'booked', booking: outcome.booking })
        return
      case 'expired':
        // The TTL lapsed under the confirm: the Seats are released, exactly as a
        // countdown reaching zero would leave them.
        onExpired()
        return
      case 'alreadyConfirmed':
        // This Hold already became a Booking — nothing to retry. Point the buyer
        // at their bookings rather than offer another payment.
        setPhase({ name: 'selecting' })
        setNotice({ kind: 'error', message: 'This hold is already booked — see it under Bookings.' })
        return
      default:
        // A transient failure: keep the Hold live so its countdown runs on and the
        // buyer can try paying again, rather than losing Seats to a network blip.
        setPhase({ name: 'held', hold })
        setNotice({ kind: 'error', message: outcome.message })
    }
  }

  /** A lapsed Hold releases its Seats and drops the buyer back to selecting. */
  function onExpired() {
    setPhase({ name: 'selecting' })
    setNotice({ kind: 'expired' })
  }

  return (
    <div className="space-y-4">
      <SeatGrid
        seats={shownSeats}
        selected={selected}
        heldByYou={yourSeatIds}
        interactive={phase.name === 'selecting'}
        onToggle={toggle}
      />

      {notice !== null && <NoticeLine notice={notice} />}

      {phase.name === 'booked' ? (
        <BookingConfirmation flight={flight} booking={phase.booking} />
      ) : phase.name === 'held' || phase.name === 'confirming' ? (
        <HeldPanel
          hold={phase.hold}
          confirming={phase.name === 'confirming'}
          onConfirm={confirm}
          onExpired={onExpired}
        />
      ) : (
        <SelectionBar
          count={selected.size}
          pricePerSeat={flight.flashPrice}
          signedIn={session !== null}
          holding={phase.name === 'holding'}
          onHold={hold}
        />
      )}
    </div>
  )
}

/** The current buyer's Seats across the phases that have any — for the "held by you" highlight. */
function ownSeatIds(phase: Phase): ReadonlySet<string> | null {
  if (phase.name === 'held' || phase.name === 'confirming') {
    return new Set(phase.hold.seats.map((seat) => seat.seatId))
  }

  if (phase.name === 'booked') {
    return new Set(phase.booking.seats.map((seat) => seat.seatId))
  }

  return null
}

/** The action row under the map while the buyer is choosing Seats. */
function SelectionBar({
  count,
  pricePerSeat,
  signedIn,
  holding,
  onHold,
}: {
  count: number
  pricePerSeat: number
  signedIn: boolean
  holding: boolean
  onHold: () => void
}) {
  const navigate = useNavigate()
  const location = useLocation()

  if (count === 0) {
    return (
      <p className="text-muted-foreground text-sm">
        Select one or more available seats to hold them.
      </p>
    )
  }

  const total = formatPrice(pricePerSeat * count)
  const seatWord = count === 1 ? 'seat' : 'seats'

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border bg-card p-4">
      <p className="text-sm">
        <span className="font-medium tabular-nums">
          {count} {seatWord}
        </span>{' '}
        · <span className="tabular-nums">{total}</span>
      </p>

      {signedIn ? (
        <Button onClick={onHold} disabled={holding}>
          {holding ? 'Holding…' : `Hold ${count} ${seatWord}`}
        </Button>
      ) : (
        <Button
          onClick={() => navigate('/login', { state: { from: `${location.pathname}${location.search}` } })}
        >
          Sign in to hold seats
        </Button>
      )}
    </div>
  )
}

/** The held Seats, a live countdown to their TTL, and the button that pays for them. */
function HeldPanel({
  hold,
  confirming,
  onConfirm,
  onExpired,
}: {
  hold: Hold
  confirming: boolean
  onConfirm: () => void
  onExpired: () => void
}) {
  const now = useNow()
  const remaining = new Date(hold.expiresAt).getTime() - now.getTime()

  // Fire once, when the countdown crosses zero: the read side treats the Hold as
  // expired from this instant, so the buyer is told and the Seats are released.
  useEffect(() => {
    if (remaining > 0) {
      return
    }

    onExpired()
    // Only the crossing matters; onExpired swaps this panel out on the same tick.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [remaining <= 0])

  const seatList = hold.seats.map((seat) => seat.seatNumber).join(', ')
  const total = formatPrice(hold.pricePerSeat * hold.seats.length)

  return (
    <div className="flex flex-wrap items-end justify-between gap-3 rounded-lg border bg-card p-4">
      <div className="space-y-2">
        <p className="text-sm">
          Holding <span className="font-medium">{seatList}</span> ·{' '}
          <span className="tabular-nums">{total}</span>
        </p>
        <p role="timer" aria-live="polite" className="text-sm tabular-nums">
          <span className="text-muted-foreground">Time left: </span>
          <span className="font-medium">{humanizeDuration(Math.max(0, remaining))}</span>
        </p>
      </div>

      <Button onClick={onConfirm} disabled={confirming}>
        {confirming ? 'Confirming…' : `Confirm and pay ${total}`}
      </Button>
    </div>
  )
}

/**
 * The booking confirmation, shown in place of the countdown once payment goes
 * through. It answers the three things a buyer wants after paying — which Seats,
 * which flight, and what they paid — from the Booking Ordering returned; the
 * flight is the one this page already loaded.
 */
function BookingConfirmation({ flight, booking }: { flight: Flight; booking: Booking }) {
  const seatList = booking.seats.map((seat) => seat.seatNumber).join(', ')

  return (
    <div role="status" className="space-y-4 rounded-lg border border-primary/40 bg-primary/5 p-5">
      <div className="space-y-1">
        <h3 className="font-heading text-lg font-semibold tracking-tight">Booking confirmed</h3>
        <p className="text-muted-foreground text-sm">
          Confirmed {formatDateTime(booking.confirmedAt)}. Your seats are yours.
        </p>
      </div>

      <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-[auto_1fr]">
        <dt className="text-muted-foreground">Flight</dt>
        <dd>
          {formatRoute(flight.origin, flight.destination)} · {flight.flightNumber}
        </dd>
        <dt className="text-muted-foreground">Seats</dt>
        <dd className="font-medium tabular-nums">{seatList}</dd>
        <dt className="text-muted-foreground">Price paid</dt>
        <dd className="font-medium tabular-nums">{formatPrice(booking.pricePaid)}</dd>
      </dl>

      <div className="flex flex-wrap gap-3">
        <Button asChild>
          <Link to="/bookings">View your bookings</Link>
        </Button>
        <Button asChild variant="outline">
          <Link to="/">Back to flights</Link>
        </Button>
      </div>
    </div>
  )
}

function NoticeLine({ notice }: { notice: Notice }) {
  if (notice.kind === 'conflict') {
    const taken = notice.seats.map((seat) => seat.seatNumber).join(', ')

    return (
      <p role="alert" className="text-destructive text-sm">
        {taken} {notice.seats.length === 1 ? 'was' : 'were'} taken before your hold went through. Pick
        different seats and try again.
      </p>
    )
  }

  if (notice.kind === 'expired') {
    return (
      <p role="status" className="text-muted-foreground text-sm">
        Your hold ran out of time and the seats were released. Select seats to try again.
      </p>
    )
  }

  return (
    <p role="alert" className="text-destructive text-sm">
      {notice.message}
    </p>
  )
}

const STATUS_CELL: Record<SeatStatus, string> = {
  Available: 'border-border bg-background text-foreground',
  Held: 'border-transparent bg-secondary text-secondary-foreground',
  Confirmed: 'border-transparent bg-muted text-muted-foreground line-through',
}

const SELECTED_CELL = 'border-primary bg-primary text-primary-foreground ring-2 ring-primary'
const HELD_BY_YOU_CELL = 'border-primary bg-primary/15 text-foreground ring-1 ring-primary'

function SeatGrid({
  seats,
  selected,
  heldByYou,
  interactive,
  onToggle,
}: {
  seats: Seat[]
  selected: ReadonlySet<string>
  heldByYou: ReadonlySet<string> | null
  interactive: boolean
  onToggle: (seatId: string) => void
}) {
  const rows = groupByRow(seats)

  return (
    <div className="w-fit space-y-2 rounded-lg border bg-card p-4">
      {rows.map(([rowNumber, rowSeats]) => (
        <div key={rowNumber} className="flex items-center gap-2">
          <span className="text-muted-foreground w-6 text-right text-xs tabular-nums">{rowNumber}</span>
          {rowSeats.map((seat) => (
            <SeatCell
              key={seat.seatId}
              seat={seat}
              selected={selected.has(seat.seatId)}
              heldByYou={heldByYou?.has(seat.seatId) ?? false}
              interactive={interactive}
              onToggle={onToggle}
            />
          ))}
        </div>
      ))}
    </div>
  )
}

const CELL_BASE =
  'inline-flex h-9 w-9 items-center justify-center rounded-md border text-xs font-medium tabular-nums'

function SeatCell({
  seat,
  selected,
  heldByYou,
  interactive,
  onToggle,
}: {
  seat: Seat
  selected: boolean
  heldByYou: boolean
  interactive: boolean
  onToggle: (seatId: string) => void
}) {
  // A Seat this buyer is holding gets its own look even though the server reports
  // it Held like any other — the map cannot say who holds it, but the checkout can.
  if (heldByYou) {
    return (
      <span
        data-status={seat.status}
        aria-label={`Seat ${seat.seatNumber}, held by you`}
        title={`${seat.seatNumber} — held by you`}
        className={`${CELL_BASE} ${HELD_BY_YOU_CELL}`}
      >
        {seat.column}
      </span>
    )
  }

  // Only Available Seats are selectable, and only while choosing; every other
  // Seat is inert text, exactly as the browse-only map rendered it.
  if (interactive && seat.status === 'Available') {
    return (
      <button
        type="button"
        aria-pressed={selected}
        aria-label={`Seat ${seat.seatNumber}, Available`}
        title={`${seat.seatNumber} — ${selected ? 'selected' : 'available'}`}
        onClick={() => onToggle(seat.seatId)}
        className={`${CELL_BASE} cursor-pointer transition-colors ${
          selected ? SELECTED_CELL : `${STATUS_CELL.Available} hover:border-primary`
        }`}
      >
        {seat.column}
      </button>
    )
  }

  return (
    <span
      data-status={seat.status}
      aria-label={`Seat ${seat.seatNumber}, ${seat.status}`}
      title={`${seat.seatNumber} — ${seat.status}`}
      className={`${CELL_BASE} ${STATUS_CELL[seat.status]}`}
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

/** Selection minus the Seats a conflict just claimed, so what remains is holdable. */
function without(selected: ReadonlySet<string>, taken: ConflictingSeat[]): Set<string> {
  const next = new Set(selected)

  for (const seat of taken) {
    next.delete(seat.seatId)
  }

  return next
}

/** The taken-Seat overlay grown by what a conflict just reported. */
function withStatuses(
  taken: ReadonlyMap<string, SeatStatus>,
  reported: ConflictingSeat[],
): Map<string, SeatStatus> {
  const next = new Map(taken)

  for (const seat of reported) {
    next.set(seat.seatId, seat.status)
  }

  return next
}

/** The loaded Seats with the taken overlay applied — the status the buyer should see. */
function applyStatuses(seats: Seat[], taken: ReadonlyMap<string, SeatStatus>): Seat[] {
  return seats.map((seat) => {
    const status = taken.get(seat.seatId)

    return status === undefined ? seat : { ...seat, status }
  })
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
