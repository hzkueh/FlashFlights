# 13 — Ordering accepts Holds outside the sale window

**What to build:** Ordering refuses a Hold on a Flight whose flash sale is not open, so the rule that a Hold is a claim on a *flash price* is kept by the service that grants Holds rather than by the page that offers them.

**Blocked by:** none.

**Status:** ready-for-human

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

- [x] Ordering knows each Flight's sale window without a synchronous call to Catalog.
- [x] `POST /holds` refuses a Flight whose window is not open, with a distinguishable outcome (not a bare 400) the SPA can render.
- [x] The decision for a Flight Ordering has no window for is explicit, tested, and written down.
- [x] Confirming a Hold granted inside the window still succeeds after the window closes.
- [x] A concurrency test covers a Hold posted as the window closes under it.

## Comments

**Built on `ticket-13-ordering-sale-window`.** Ordering consumes the same
`FlightSaleStarted` Notifications does and keeps a `SaleAnnouncement` row
(FlightId, SaleEndsAt); `HoldService.CreateHoldAsync` refuses unless that row
exists and the window is still open. No synchronous call to Catalog, and
`OrderingBoundaryTests` still holds — Ordering references no Catalog assembly.

**The open question, decided: no window heard means no Hold**
([ADR-0003](../../../docs/adr/0003-ordering-refuses-a-hold-it-has-no-sale-window-for.md)).
The two consumers of the same event now take opposite defaults for an unknown
Flight, deliberately. Accepting the unknown here would have left the **Upcoming**
half of this report wide open — an upcoming sale has no announcement *by
definition* — so it would have closed only the Ended case. The cost of refusing is
bounded and small: Catalog announces every crossing exactly once, including a
window already open or already closed when first seen, so a sale seeded straight
into the database becomes holdable one `SaleStartScheduler` tick (10s) after
Catalog starts, not never. `seed-demo-data.py`'s docstring gained that as
gotcha 4.

**Confirm is deliberately not gated.** The Hold *is* the claim and it was granted
while the price was on offer; the TTL is what ends it. Gating confirm would have
to gate on `Hold.CreatedAt`, which is inside the window by construction — it would
refuse nothing and cost a buyer their seats mid-payment. Tested as its own case.

**The refusal is a 409 carrying `reason: "saleNotOpen"`**, alongside confirm's
existing `expired` / `alreadyConfirmed`, with prose that distinguishes an ended
sale from one still to come. Not a 400 (the request is well formed, and an
upcoming sale is worth posting to again later) and not a 404 (Ordering owns no
Flights and cannot say whether one exists). The SPA turns it into its own
`saleNotOpen` outcome and drops the whole selection rather than offering "pick
different seats" — unlike a lost race, no other Seat would have fared better.

**Where the check sits matters.** It runs inside the granting transaction against
the same `clock.GetUtcNow()` that stamps the Hold, so a request cannot pass the
window check at one instant and be granted as of a later one. The new concurrency
test pins it: twenty parallel posts, each on its own Seat, while the clock steps
across `SaleEndsAt` — exactly the ten inside the window win, and no Hold row
exists with `CreatedAt >= SaleEndsAt`.

**One operational sharp edge, found while checking the running stack and written
into ADR-0003.** Catalog marks each crossing announced, so a Flight it announced
*before* Ordering learned to consume is never backfilled — Ordering refuses Holds
on it for good. Every flight in the current compose stack is in that state
(`SaleStartHandledAt` set on all nine). The recovery is a replay, not a backfill:
clear `SaleStartHandledAt` and the next scan re-announces, which is safe because
the announcement was always at-least-once and both consumers are idempotent. A
fresh clone never meets this. `seed-demo-data.py` now says so.

**From code review.** Two fixes to user-facing and explanatory text, one to
structure:

- The "not started" prose asserted something Ordering cannot know. It never
  learns `SaleStartsAt`, only that no open window has been announced to it — so
  "this sale has not started yet" is wrong for the case where the announcement is
  simply a moment behind, told to a buyer looking at a page that says Live. It now
  hedges exactly where the uncertainty is.
- `SaleWindowState`'s doc claimed it was "named from the buyer's side", but the
  buyer's words are Catalog's Upcoming/Live/Ended. The names stay — Ordering
  cannot claim "Upcoming" when all it knows is that it has heard nothing — and the
  reasoning now says that instead.
- `HoldEndpoints.SaleNotOpen` branched on the same state twice, once for the title
  and once for the prose; one branch now yields both so they cannot drift.

Considered and kept: the `DbUpdateException` idempotence idiom is a third copy of
the shape `WatchService` uses (services share no code by design), `AnnouncedAt` is
written but never read by the rule (it is what answers "did the announcement reach
Ordering, and when?", the first question a refused Hold raises), and the window
check still runs after the seat lock is taken — the lock is wasted on a refusal,
but moving it would split the boundary rule across two clock reads.

**Verified against the compose stack** (Ordering rebuilt, reseeded, driven through
the gateway with a real token):

| Sale state | Flight | Result |
| --- | --- | --- |
| Live | FF100 | `201` — Hold granted, then confirmed into a Booking at £149.00 |
| Ended | FF330 | `409` `reason: saleNotOpen`, "Flash sale ended", no `seats` extension |
| Upcoming | FF210 | `409` `reason: saleNotOpen`, "Flash sale not open yet" |
| Live, seat taken | FF100 2C | `409` naming the Seat — the conflict path is unchanged |

Ordering recorded exactly two windows, both learned off the bus: FF100 and FF330,
the two whose crossings Catalog announced. FF210, still Upcoming, has no row —
which is precisely why "no window heard" has to refuse. The live run also caught a
log line claiming "holds are open for it" on an announcement whose window had
already closed; it now states the window instead of asserting the conclusion.

The SPA's own gate from the previous branch stays. It is not redundant: it keeps a
buyer from being shown a button that cannot work, and the flow now handles the
instant where page and service disagree instead of treating it as impossible.
