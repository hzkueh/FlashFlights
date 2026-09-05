# 07 — Checkout: select seats, hold, confirm, book

**What to build:** A signed-in User picks Seats on the map, requests a Hold, watches a countdown, confirms through the simulated payment step, and lands on a booking confirmation — and can see their past Bookings.

**Blocked by:** 04, 06.

**Status:** in-progress (paused mid-ticket after the countdown; confirm/booking/history remain)

- [x] Signed-in User can select one or more Available Seats on the map.
- [x] Requesting a Hold calls `Ordering` and reserves exactly those Seats.
- [x] A losing race is surfaced clearly — the User is told which Seats were taken and returned to a usable state, never shown a false success.
- [x] Live countdown of the Hold's remaining time; expiry releases the Seats and tells the User.
- [ ] Confirm step completes the simulated payment and produces a Booking.
- [ ] Booking confirmation screen shows Seats, flight, and price paid.
- [ ] Booking history page lists the User's past Bookings.
- [x] Signed-out visitors are prompted to sign in when they try to hold Seats.

## Comments

### Paused after the countdown, on purpose

This slice stops at the live countdown checkbox. The buyer can select Seats,
hold exactly those, be told cleanly when they lose the race, and watch the Hold
count down until it expires and releases — the whole pre-payment loop. Confirm
(the simulated payment → Booking), the confirmation screen, and booking history
are the next slice and are untouched. The signed-out sign-in prompt was pulled
forward with this slice because it is inseparable from "try to hold".

### Implementation notes

- **All frontend.** Ordering's Hold HTTP surface already exists from ticket 05
  (`POST /api/ordering/holds`, 201 `HoldView` / 409 with a `seats` extension /
  400 validation-problem). No backend change; this ticket is the SPA caller.
- **The seam is `web/src/lib/holds.ts`** (`createHold`), which maps each status
  Ordering answers with onto a `CreateHoldOutcome` the checkout branches on —
  keeping the load-bearing distinction from ticket 05 (conflict = lost a race,
  retry may help; invalid = malformed, retry cannot; error = unreachable).
  TDD'd red→green.
- **Checkout lives on the flight detail page** (`web/src/components/seat-checkout.tsx`),
  not a separate route — App.tsx left that open and a step on the seat map keeps
  the map, selection, and countdown in one place, which ticket 08's live updates
  will build straight onto.
- **No map re-read on this buyer's own actions.** A granted Hold's Seats render
  as "held by you" from the Hold itself, and a lapsed Hold's Seats were already
  Available in the map we loaded — so nothing forces a refetch, which would have
  unmounted the countdown. Other buyers' changes still need a reload until
  ticket 08's SignalR lands — same limitation ticket 06 documented for browsing.
- **Live-verified** (Vite dev server against the running compose stack): map
  renders, selection + running total, and the signed-out → sign-in prompt →
  login flow. The authenticated hold/countdown/conflict/expiry paths are covered
  by integration tests through the whole app (`web/src/routes/checkout.test.tsx`)
  against the confirmed contract; a live authenticated pass needs a test login.
- **Tests:** frontend 71 (was 61) — `holds.ts` unit (5) and the checkout flow
  through the app (5).
