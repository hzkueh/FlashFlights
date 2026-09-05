# FlashFlights

An airline flash-sale ticketing app. Buyers browse a small catalog of flights
each running a time-boxed flash sale, pick specific seats on a live seat map,
and book at the flash price before the sale window closes or the seats run
out. The system exists to prove safe concurrent booking and real-time
consistency across service boundaries — a deliberate companion piece to
[RestaurantInventory](../RestaurantInventory/CONTEXT.md), proving different
skills (real-time, distributed concurrency, a public-facing SPA) rather than
repeating them.

## Language

**Flight**:
The flash-sale unit for sale — a route, departure time, FlashPrice, and its
allocation of Seats, running one sale window (SaleStartsAt/SaleEndsAt). One
flash sale per Flight; not a separate concept from it.
_Avoid: FlashSale, Route, Trip_

**ReferenceFare**:
The standing, non-sale price a Flight's FlashPrice is marked down from —
what a Seat would cost outside the flash window. Display-only: it is never
charged, never the price paid, and never the basis for a Hold. Exists solely
so the UI can show how much a buyer saves; the discount percentage is
computed, never stored — `(ReferenceFare − FlashPrice) / ReferenceFare`. A
Flight may have none, in which case no saving is shown. Must be greater than
FlashPrice when present.
_Avoid: BasePrice, OriginalPrice, RegularPrice, ListPrice, MSRP, Discount
(the saving is derived, not an entity)_

**Seat**:
An individual, uniquely identified position within a Flight's flash-sale
allocation (e.g. `12A`). Fixed once seeded.
_Avoid: SeatNumber (fine as an attribute, not the entity), Slot_

**SeatMovement**:
An append-only record of a single change to one Seat. Type is one of
**Held** (a buyer started checkout on it), **Released** (a Held expired
without confirming), or **Confirmed** (a Held became a Booking). The source
of truth for a Seat's status — never edited or deleted. See
[ADR-0001](docs/adr/0001-seat-movement-ledger.md).
_Avoid: Transaction, Reservation event, Log_

**SeatStatus**:
The *computed* condition of a Seat — Available, Held, or Confirmed — derived
from its latest SeatMovement (an unresolved Held past its TTL computes back
to Available). Never a stored, editable field.
_Avoid: State, SeatState_

**SeatCounts**:
The advisory per-Flight tally of how many of its Seats are Available, Held,
and Confirmed, shown while browsing. Always trails the SeatMovements it is
derived from, so it can disagree with the SeatStatus of the very Seats it
counts — never the basis for granting a Hold.
_Avoid: Availability, Inventory, Stock_

**Hold**:
A buyer's in-progress reservation on one or more specific Seats, created by
posting Held SeatMovements for them. Carries an expiry (TTL). Resolves into
either a Booking (Confirmed) or expiry (Released) — its status is computed
from whether a resolving SeatMovement exists yet, never stored directly.
_Avoid: Reservation, Cart, Basket_

**Booking**:
A Hold that resolved into Confirmed — the buyer's completed flash-sale
purchase, referencing the specific Seats bought, the User, and the price
paid.
_Avoid: Order, Ticket, Purchase — especially avoid "Ticket," which reads as
the real-world boarding document rather than this purchase record_

**Watch**:
A User's subscription to be notified when a specific Flight's sale goes
live.
_Avoid: Alert, Subscription, Follow_

**Notification**:
A persisted, delivered alert to a User (e.g. "Flight X's flash sale is now
live") — pushed live if the User is connected, and stored so it's still
visible on their next visit regardless.
_Avoid: Message_

**User**:
An authenticated buyer. The only actor in the system — there is no separate
admin role; flights are seeded, not authored in-app.
_Avoid: Customer, Account_

## Out of scope (deliberately)

- **Multiple flash-sale windows per Flight.** One sale window, one Flight.
- **Cancellations or refunds** on a confirmed Booking.
- **Admin-authored flights.** Seed data only; no flight-authoring UI.
- **Low-stock / sold-out Watch triggers.** A Watch fires only when its
  Flight's sale goes live — other triggers are a possible later stretch.
- **Real payment processing.** Booking confirmation is a stubbed step.
