# Notifications learns a sale has opened from the announcement, never by asking Catalog

A Watch may only be created while its Flight's sale is still ahead, so the
service accepting one has to know whether that moment has passed. Catalog owns
Flights and their sale windows, so the obvious reading is that Notifications
should ask it. We chose the opposite: when the dispatcher consumes a
`FlightSaleStarted` it writes a **SaleAnnouncement** row — FlightId and the
moment it was announced — and `WatchService` refuses a later Watch with a 409 by
reading that row alone. Notifications never calls Catalog, at Watch time or any
other.

This is the same shape as the seat-map relay reading a Seat's new status straight
off the movement event rather than querying Ordering for it — a service learns
what it needs from the event it already consumes, and keeps a local record of
what it learned. The alternative, a synchronous call to Catalog on every Watch,
would make watching fail whenever Catalog is down, for a rule that the event
stream already answers.

SaleAnnouncement is bookkeeping, not domain language: it is how this service
remembers something it was told, and no buyer would name it. It is deliberately
absent from `CONTEXT.md`'s glossary, which records the **Watch** rule it enforces
and points here for the mechanism.

## Consequences

- Catalog announces every crossing of `SaleStartsAt`, including a window that had
  already closed when it was first seen — an already-ended sale in seed data, or
  a service down across the whole window. It does not decide who hears about it.
  The announcement therefore carries `SaleEndsAt`, and Notifications is the one
  that turns a closed window into "tell no one" while still recording that the
  sale's moment has passed.
- Judging the window in Catalog instead — announcing only a sale still open —
  would leave Notifications unable to tell an ended sale from one that has not
  opened, and so willing to accept a Watch that could never fire.
- The refusal is only as current as the bus. A Watch arriving between a sale
  opening and its announcement being consumed is accepted, and then fires
  immediately when the announcement lands. That is the correct outcome, not a
  race to close: the User is told the sale is live, which is what they asked for.
- A Watch on a FlightId that is not a Flight is accepted and simply never fires.
  Costs one row; preventing it is exactly the synchronous dependency this
  decision refuses.
- `FlightSaleStarted` is published at-least-once (the `Flight.SaleStartHandledAt`
  marker is written after the publish), so both consumers of it must be
  idempotent. The inbox's unique index on (UserId, FlightId) and
  SaleAnnouncement's FlightId primary key are what make the *effect* exactly-once.
