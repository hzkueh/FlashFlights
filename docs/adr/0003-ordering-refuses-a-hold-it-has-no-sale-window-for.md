# Ordering learns each Flight's sale window from the announcement, and refuses a Hold it has no window for

A Hold is a claim on a **flash price** (CONTEXT.md), so it may only be granted
while that price is on offer — inside the Flight's sale window. Catalog owns the
window and the SPA renders it, but until now nothing between the two enforced it:
Ordering granted Holds from the Seat ledger alone and had never been told a
Flight has a window at all. A `POST /holds` with any ended Flight's id and a
valid token was answered `201 Created`.

Ordering now consumes the same `FlightSaleStarted` Notifications does and writes
a **SaleAnnouncement** row — FlightId and the `SaleEndsAt` the announcement
carries. `HoldService.CreateHoldAsync` refuses unless that row exists *and* the
window has not yet closed. This is ADR-0002's shape again, and for the same
reason: a service learns what it needs from the event it already consumes rather
than asking the owning service. The synchronous alternative — Ordering asking
Catalog per Hold — would put a second service on the one path this system exists
to prove safe under concurrency, and make holding fail whenever Catalog is down.

## The decision underneath it: no window heard means no Hold

The two consumers of `FlightSaleStarted` take **opposite defaults** for a Flight
they have never heard an announcement for, and that is deliberate.

Notifications accepts an unknown Flight: a Watch on an id that is not a Flight
costs one row and simply never fires. Ordering refuses one. The asymmetry is in
what the unknown case costs when it is wrong:

- **Accepting** an unknown Flight here leaves the reported hole open for exactly
  the Flights most likely to be in it. An **Upcoming** sale has no announcement
  *by definition* — that is what upcoming means — so "unknown means allowed"
  would grant a Hold on every sale that has not opened yet, which is half of what
  this ticket reported. A Hold is a claim on money; an unfired Watch is not.
- **Refusing** costs a Flight its Holds for as long as Ordering has not consumed
  its announcement. That window is bounded and small: Catalog announces *every*
  crossing of `SaleStartsAt` exactly once, including a window already open — or
  already closed — when it was first seen, such as a sale seeded straight into
  the database (ADR-0002). So a seeded live sale becomes holdable one
  `SaleStartScheduler` tick after Catalog starts, not never.

The refusal is a distinguishable 409 carrying `reason: "saleNotOpen"`, not a bare
400: the request is well formed, and an Upcoming sale is even worth retrying once
it opens. It is not a 404 — Ordering owns no Flights and cannot say whether one
exists, only whether it has heard this Flight's sale open.

## Consequences

- **`SaleAnnouncement` is bookkeeping, not domain language** — the same name and
  the same role it has in Notifications: how a service remembers something it was
  told. Ordering's copy also keeps `SaleEndsAt`, because it needs the window's
  end and not merely the fact of the opening. It is deliberately absent from
  `CONTEXT.md`'s glossary, which records the **Hold** rule it enforces and points
  here for the mechanism.

- **Ordering's refusal trails the bus, exactly as Notifications' does.** A Hold
  arriving between a sale opening and its announcement being consumed is refused,
  and succeeds on the retry a moment later. That is the conservative direction of
  the same staleness ADR-0002 accepted in the permissive direction, and it is the
  right way round for a write that takes a buyer's money.

- **The window is checked against the same `now` that stamps the Hold**, inside
  the transaction that grants it, next to the expiry boundary every other path
  shares (`SeatStatusRules.HasExpired`). Checking earlier with an earlier clock
  read would let a Hold pass the window check at one instant and be stamped
  `CreatedAt` at a later one — the sub-second version of the very bug this
  closes.

- **Confirm is not gated, on purpose.** A Hold granted inside the window still
  confirms after the window closes. The Hold *is* the claim on the flash price,
  and it was granted while that price was on offer; stranding a buyer mid-payment
  because the clock ran out during checkout is worse than honouring the TTL they
  were promised. The TTL already bounds how far past the close this can reach, and
  `CreateHoldAsync` is the one gate because creation time is when the claim is
  made. Gating confirm as well would have to gate on `Hold.CreatedAt` — which is
  already inside the window by construction, so it would refuse nothing and cost
  a buyer their seats.

- **A redelivered announcement changes nothing.** `SaleAnnouncement`'s FlightId
  primary key makes the recording idempotent, which it must be: the announcement
  is published at-least-once (ADR-0002).

- **A Flight announced before Ordering began consuming is never backfilled.**
  Catalog announces each crossing exactly once and marks it
  (`Flight.SaleStartHandledAt`), so a deployment whose Catalog had already
  announced everything before this shipped has Flights that Ordering will never
  hear about — and therefore refuses Holds on for good. The recovery is to replay
  rather than to backfill: clearing `SaleStartHandledAt` makes the next scan
  announce those crossings again, which is safe precisely because the
  announcement was always at-least-once (ADR-0002) and both consumers are
  idempotent — Ordering's row is keyed by FlightId, and Notifications notifies
  only watchers with no Notification row, of whom there are none, since a Watch
  is already refused once a sale has been announced. A fresh clone never meets
  this: Ordering consumes from the first crossing onward.

- **Catalog and Ordering must agree on the boundary.** Both treat the window as
  inclusive at the start and exclusive at the end — at the instant it closes the
  sale is Ended and a Hold is refused — matching `FlightCatalogService.StateOf`
  and the SPA's `saleStateAt`. A change to one belongs in the others.

- **Ordering still references no Catalog code.** The window arrives as an event
  payload, so `OrderingBoundaryTests` stays true: Ordering cannot read Catalog's
  projection, and now does not need to.
