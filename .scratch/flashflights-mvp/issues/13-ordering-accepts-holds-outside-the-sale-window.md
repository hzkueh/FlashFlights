# 13 — Ordering accepts Holds outside the sale window

**What to build:** Ordering refuses a Hold on a Flight whose flash sale is not open, so the rule that a Hold is a claim on a *flash price* is kept by the service that grants Holds rather than by the page that offers them.

**Blocked by:** none.

**Status:** needs-triage

## The gap

Ordering grants Holds from the Seat ledger alone and has never been told a Flight
has a sale window — `SaleEndsAt`, `SaleStartsAt` and `SaleState` appear nowhere in
`src/FlashFlights.Ordering/`. Catalog computes the window and the SPA renders it,
but nothing between the two enforces it.

Reproduced against the compose stack on 2026-09-15, on FF210 SFO→JFK whose sale
had ended days earlier:

```
POST /api/ordering/holds  →  201 Created
```

The SPA then offered "Confirm and pay £299.00", so a Booking could be written
against a sale that was over. `POST /api/ordering/holds` with a `seatIds` body is
enough to reproduce with any ended flight's id and a valid token; the seat map
read is unauthenticated, so finding a seat id costs nothing.

The same holds for an **Upcoming** sale: the price is not on offer yet, and the
Hold is granted anyway.

## What is already done

The SPA no longer *offers* it (this branch): `web/src/components/seat-checkout.tsx`
gates selection and the hold button on `saleState === 'Live'`, deriving that state
from the window via `web/src/lib/sale-window.ts` so it closes on the window
closing rather than on a stale server snapshot. That is the page declining to
offer what the domain disallows — **not** where the rule is kept. This ticket is
the rule.

## Approach to weigh

The established pattern is ADR-0002: a service learns what it needs from the event
it already consumes rather than asking the owning service. `FlightSaleStarted`
already carries `SaleEndsAt`, exactly as Notifications needs it, so Ordering could
consume the same announcement and keep its own record of each Flight's window —
no synchronous call to Catalog on the hot booking path, and no new coupling that
makes holding fail whenever Catalog is down.

The synchronous alternative (Ordering asks Catalog per hold) is the one ADR-0002
rejected for Notifications, and it lands on the one path this system exists to
prove safe under concurrency.

Open questions for whoever picks this up:

- **A Flight Ordering has never heard an announcement for.** Notifications accepts
  the Watch and lets it never fire, at a cost of one row. Ordering refusing an
  unknown Flight would make Holds fail until the announcement is consumed
  (including for a sale seeded straight into the database, as the demo seed does);
  accepting it leaves exactly this hole open for that Flight. This is the decision
  the ticket turns on, and it likely wants its own ADR alongside 0001 and 0002.
- **A Hold granted while live, confirmed after the close.** The SPA deliberately
  lets an in-flight Hold resolve — the Hold was granted inside the window and
  stranding a buyer mid-checkout is worse than the two minutes of slack. If
  Ordering gates `confirm` as well as `create`, it should gate on when the Hold was
  *created*, not when the payment lands.
- `Seat` and the ledger stay as they are either way: the window belongs to the
  Flight, not the Seat.

## Checklist

- [ ] Ordering knows each Flight's sale window without a synchronous call to Catalog.
- [ ] `POST /holds` refuses a Flight whose window is not open, with a distinguishable outcome (not a bare 400) the SPA can render.
- [ ] The decision for a Flight Ordering has no window for is explicit, tested, and written down.
- [ ] Confirming a Hold granted inside the window still succeeds after the window closes.
- [ ] A concurrency test covers a Hold posted as the window closes under it.
