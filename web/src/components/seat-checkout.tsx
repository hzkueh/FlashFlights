import { motion } from 'motion/react'
import { useEffect, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router'

import { Countdown } from '@/components/countdown'
import { GatewayErrorPanel, StatePanel } from '@/components/state-panel'
import { Button } from '@/components/ui/button'
import { LoadingPanel } from '@/components/loading-panel'
import { Skeleton } from '@/components/ui/skeleton'
import { useAsync } from '@/hooks/use-async'
import { useLiveSeatMap } from '@/hooks/use-live-seat-map'
import { useNow } from '@/hooks/use-now'
import { useRecentlyChanged } from '@/hooks/use-recently-changed'
import { useReducedMotion } from '@/hooks/use-reduced-motion'
import { useSession } from '@/hooks/use-session'
import { type Booking, type ConfirmHoldOutcome, confirmHold } from '@/lib/bookings'
import type { Flight, SaleState } from '@/lib/catalog'
import { formatDateTime, formatPrice, formatRoute, humanizeDuration } from '@/lib/format'
import {
  type ConflictingSeat,
  type CreateHoldOutcome,
  type Hold,
  createHold,
} from '@/lib/holds'
import { type Seat, type SeatStatus, getSeatMap } from '@/lib/seat-map'
import { holdUrgency } from '@/lib/urgency'
import { cn } from '@/lib/utils'

/**
 * The seat map turned interactive: a signed-in buyer selects Available Seats,
 * requests a Hold on exactly those, and then watches it count down. The map is
 * seeded from Ordering's one-off HTTP read (ADR-0001) and then kept live over
 * SignalR (ticket 08), so a Seat another buyer holds, confirms, or lets expire
 * repaints here without a refetch; this buyer's own Hold is reflected from the
 * granted Hold itself — its Seats render as "held by you".
 */
export function SeatCheckout({ flight, saleState }: { flight: Flight; saleState: SaleState }) {
  const map = useAsync((signal) => getSeatMap(flight.id, signal), flight.id)

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
        <h2 className="font-heading text-lg font-semibold tracking-tight">Seat map</h2>
        <SeatLegend />
      </div>

      {map.status === 'loading' && <SeatMapSkeleton />}

      {map.status === 'error' && (
        <GatewayErrorPanel what="the seat map" hint="It updates live once Ordering is reachable." />
      )}

      {map.status === 'ready' &&
        (map.data.seats.length === 0 ? (
          <StatePanel>
            <p className="text-sm">This flight has no seats yet.</p>
          </StatePanel>
        ) : (
          <LiveCheckout flight={flight} saleState={saleState} initialSeats={map.data.seats} />
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
function LiveCheckout({
  flight,
  saleState,
  initialSeats,
}: {
  flight: Flight
  saleState: SaleState
  initialSeats: Seat[]
}) {
  const seats = useLiveSeatMap(flight.id, initialSeats)

  return <Checkout flight={flight} saleState={saleState} seats={seats} />
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

/** Nothing selected — hoisted so a closed sale does not build a new Set each render. */
const NO_SEATS: ReadonlySet<string> = new Set()

/** A message layered over selection: a lost race, a plain failure, or a lapsed Hold. */
type Notice =
  | { kind: 'conflict'; seats: ConflictingSeat[] }
  | { kind: 'expired' }
  | { kind: 'error'; message: string }

function Checkout({
  flight,
  saleState,
  seats,
}: {
  flight: Flight
  saleState: SaleState
  seats: Seat[]
}) {
  const { session } = useSession()
  const [phase, setPhase] = useState<Phase>({ name: 'selecting' })
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set())
  const [notice, setNotice] = useState<Notice | null>(null)
  // What a lost race told us about Seats others took, laid over the live map so a
  // just-taken Seat renders as taken (not a stale Available button) the instant
  // the 409 comes back — ahead of, and belt-and-braces with, the SignalR push
  // that will report the same change.
  const [taken, setTaken] = useState<ReadonlyMap<string, SeatStatus>>(new Map())

  // Seats can only be held while the window is open — a Hold is a claim on a
  // flash price, and outside the window there is no flash price to claim. An
  // Upcoming sale is refused for the same reason as an Ended one: the window is
  // the offer, and it is not open. Everything else on the page still reads, so a
  // closed sale is browsable in full — CONTEXT.md's seat map is a view of the
  // ledger, not a shop front.
  //
  // This is the page declining to offer what the domain does not allow, NOT
  // where the rule is kept: Ordering grants Holds from the Seat ledger alone and
  // has never been told a Flight has a sale window, so a POST to /holds outside
  // one is still granted. Closing that needs Ordering to learn the window the
  // way Notifications learns it (ADR-0002), which is a decision, not a guard.
  const saleOpen = saleState === 'Live'
  const shownSeats = taken.size === 0 ? seats : applyStatuses(seats, taken)
  // A selection made a moment before the window closed stops counting with it,
  // rather than staying lit under a bar that will no longer act on it.
  const shownSelected = saleOpen ? selected : NO_SEATS
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
        selected={shownSelected}
        heldByYou={yourSeatIds}
        interactive={phase.name === 'selecting' && saleOpen}
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
      ) : saleOpen ? (
        <SelectionBar
          count={selected.size}
          pricePerSeat={flight.flashPrice}
          signedIn={session !== null}
          holding={phase.name === 'holding'}
          onHold={hold}
        />
      ) : (
        <ClosedSaleLine state={saleState} />
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

/**
 * What stands in for the action row when the window is shut. It says which way
 * the sale is shut, because the two are not the same answer to "can I buy this":
 * an upcoming sale is worth waiting for (and the Watch toggle above offers
 * exactly that), an ended one is not.
 */
function ClosedSaleLine({ state }: { state: SaleState }) {
  return (
    <p className="text-muted-foreground text-sm">
      {state === 'Upcoming'
        ? 'This flash sale hasn’t opened yet. Seats can be held once it does.'
        : 'This flash sale has ended. The seat map is read-only.'}
    </p>
  )
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
        <Countdown role="timer" urgency={holdUrgency(remaining)}>
          <span className="text-muted-foreground font-normal">Time left: </span>
          {humanizeDuration(Math.max(0, remaining))}
        </Countdown>
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
      <StatePanel
        tone="error"
        title={`${taken} ${notice.seats.length === 1 ? 'was' : 'were'} taken before your hold went through.`}
      >
        <p className="text-muted-foreground text-sm">Pick different seats and try again.</p>
      </StatePanel>
    )
  }

  if (notice.kind === 'expired') {
    return (
      // Announced: this happened to the buyer while they were looking elsewhere,
      // rather than being an answer to something they just did.
      <StatePanel role="status" title="Your hold ran out of time and the seats were released.">
        <p className="text-muted-foreground text-sm">Select seats to try again.</p>
      </StatePanel>
    )
  }

  return <StatePanel tone="error" title={notice.message} />
}

/**
 * Each SeatStatus in the theme's own words. The Held and Confirmed tokens are
 * defined for both themes in `index.css`, so a status reads the same way in each
 * — an amber seat someone is holding, a dimmed and struck-through one that is
 * gone — rather than collapsing into shades of grey in one of them.
 */
const STATUS_CELL: Record<SeatStatus, string> = {
  Available: 'border-border bg-background text-foreground',
  Held: 'border-transparent bg-seat-held text-seat-held-foreground',
  Confirmed:
    'border-transparent bg-seat-confirmed text-seat-confirmed-foreground line-through',
}

const SELECTED_CELL = 'border-primary bg-primary text-primary-foreground ring-2 ring-primary'
const HELD_BY_YOU_CELL = 'border-primary bg-primary/15 text-foreground ring-1 ring-primary'

/** The ring a seat wears for the moment after its status moves under the viewer. */
const JUST_CHANGED_CELL = 'ring-2 ring-primary/60 ring-offset-1 ring-offset-card'

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
    // The cabin keeps its real proportions at every width: a wide-bodied map
    // scrolls sideways inside the card rather than squashing its seats into
    // unreadable slivers or reflowing rows into a shape no aircraft has.
    <div className="max-w-full overflow-x-auto rounded-lg border bg-card p-4">
      <div className="w-fit space-y-2">
        {rows.map(([rowNumber, rowSeats]) => (
          <div key={rowNumber} className="flex items-center gap-1.5 sm:gap-2">
            <span className="text-muted-foreground w-6 shrink-0 text-right text-xs tabular-nums">
              {rowNumber}
            </span>
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
    </div>
  )
}

/**
 * The cabin's shape while Ordering is still answering, so the page does not jump
 * when the real map lands. Four rows of six is a stand-in, not a promise — the
 * flight's real dimensions are not known until the seats arrive.
 */
function SeatMapSkeleton() {
  return (
    <LoadingPanel
      label="Loading seats…"
      className="max-w-full overflow-x-auto rounded-lg border bg-card p-4"
    >
      <div className="w-fit space-y-2">
        {[0, 1, 2, 3].map((row) => (
          <div key={row} className="flex items-center gap-1.5 sm:gap-2">
            <Skeleton className="h-3 w-6 shrink-0" />
            {[0, 1, 2, 3, 4, 5].map((column) => (
              <Skeleton key={column} className="size-8 shrink-0 sm:size-9" />
            ))}
          </div>
        ))}
      </div>
    </LoadingPanel>
  )
}

const CELL_BASE =
  'inline-flex size-8 shrink-0 items-center justify-center rounded-md border text-xs font-medium tabular-nums transition-[background-color,border-color,color,box-shadow] duration-300 sm:size-9'

/** The attention beat a seat plays when its status moves. */
const FLASH = { scale: [1, 1.16, 1] }
const AT_REST = { scale: 1 }

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
  // What a live map cannot say on its own: which of these hundred seats just
  // moved. Without it a seat someone else took across the cabin is a silent
  // repaint, and the guarantee this app exists to demonstrate goes unseen.
  const justChanged = useRecentlyChanged(seat.status)
  const reducedMotion = useReducedMotion()
  const flashing = justChanged && !reducedMotion

  // Only Available Seats are selectable, and only while choosing; every other
  // Seat is inert text, exactly as the browse-only map rendered it.
  const selectable = interactive && !heldByYou && seat.status === 'Available'

  // The ring stays even when the scale beat is suppressed: a reader who asked for
  // less motion still needs to see which seat changed, and colour and a ring say
  // it without moving anything.
  const className = cn(
    CELL_BASE,
    heldByYou
      ? HELD_BY_YOU_CELL
      : selectable && selected
        ? SELECTED_CELL
        : selectable
          ? `${STATUS_CELL.Available} cursor-pointer hover:border-primary`
          : STATUS_CELL[seat.status],
    justChanged && JUST_CHANGED_CELL,
  )

  // A Seat this buyer is holding gets its own look even though the server reports
  // it Held like any other — the map cannot say who holds it, but the checkout can.
  const inner = selectable ? (
    <button
      type="button"
      aria-pressed={selected}
      aria-label={`Seat ${seat.seatNumber}, Available`}
      title={`${seat.seatNumber} — ${selected ? 'selected' : 'available'}`}
      onClick={() => onToggle(seat.seatId)}
      data-status={seat.status}
      data-changed={justChanged ? 'true' : 'false'}
      className={className}
    >
      {seat.column}
    </button>
  ) : (
    <span
      data-status={seat.status}
      data-changed={justChanged ? 'true' : 'false'}
      aria-label={
        heldByYou ? `Seat ${seat.seatNumber}, held by you` : `Seat ${seat.seatNumber}, ${seat.status}`
      }
      title={heldByYou ? `${seat.seatNumber} — held by you` : `${seat.seatNumber} — ${seat.status}`}
      className={className}
    >
      {seat.column}
    </span>
  )

  return (
    // The animated element is this wrapper, not the seat itself, and that is the
    // whole point of it: becoming Held turns a selectable button into inert text,
    // and React rebuilds the DOM node when the element type changes. Framer reads
    // a rebuilt node as a mount, which `initial: false` suppresses — so animating
    // the seat directly would skip the beat for Available-to-Held, precisely the
    // change worth showing. The wrapper outlives the swap.
    <motion.span
      data-seat-cell={seat.seatNumber}
      className="inline-flex shrink-0"
      // No mount animation — on load every seat is new, and a cabin that animates
      // itself into existence buries the one change that matters later.
      initial={false}
      animate={flashing ? FLASH : AT_REST}
      transition={{ duration: flashing ? 0.45 : 0.2, ease: 'easeOut' }}
      whileTap={selectable && !reducedMotion ? { scale: 0.92 } : undefined}
    >
      {inner}
    </motion.span>
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
