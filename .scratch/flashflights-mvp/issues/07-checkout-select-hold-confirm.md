# 07 — Checkout: select seats, hold, confirm, book

**What to build:** A signed-in User picks Seats on the map, requests a Hold, watches a countdown, confirms through the simulated payment step, and lands on a booking confirmation — and can see their past Bookings.

**Blocked by:** 04, 06.

**Status:** ready-for-human (all acceptance criteria met; live authenticated pass against the compose stack still owed — see notes)

- [x] Signed-in User can select one or more Available Seats on the map.
- [x] Requesting a Hold calls `Ordering` and reserves exactly those Seats.
- [x] A losing race is surfaced clearly — the User is told which Seats were taken and returned to a usable state, never shown a false success.
- [x] Live countdown of the Hold's remaining time; expiry releases the Seats and tells the User.
- [x] Confirm step completes the simulated payment and produces a Booking.
- [x] Booking confirmation screen shows Seats, flight, and price paid.
- [x] Booking history page lists the User's past Bookings.
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

### Confirm, confirmation, and history (the second slice)

- **Confirm is one new backend read plus all frontend.** Ordering's confirm HTTP
  surface already existed from ticket 05 (`POST /api/ordering/holds/{id}/confirm`,
  201 `BookingView` / 409 `reason: expired|alreadyConfirmed` / 404). Booking
  history had **no** read endpoint, so this ticket added one:
  `GET /api/ordering/bookings` (auth, scoped to the token's User) →
  200 `BookingView[]`, backed by `IBookingReadService`/`BookingReadService`
  (`src/FlashFlights.Ordering/Bookings/`). It reads the buyer's Bookings and each
  Booking's Seats from the Hold's Confirmed movements — the same ledger, formed
  in memory because `SeatNumber` is computed, never a column.
- **`BookingView` gained `FlightId`.** A Booking records its Flight only through
  its Hold; the SPA needs it to name which Flight each Booking is for. The confirm
  reply and the history list are now the same wire shape, so the SPA parses a
  Booking one way whether it just made it or is looking back at it.
- **The confirm seam is `web/src/lib/bookings.ts`** (`confirmHold`), mirroring
  `holds.ts`: it maps each answer onto a `ConfirmHoldOutcome` the checkout branches
  on, keeping the load-bearing distinction — expired (start over) vs
  alreadyConfirmed (terminal) vs a transient error (keep the Hold live so the
  countdown runs on and the buyer can retry payment without losing Seats).
- **Confirmation is a phase on the seat map, not a route.** `confirming` and
  `booked` join the checkout's phase machine (`seat-checkout.tsx`); the countdown
  never unmounts between clicking pay and the Booking arriving. The confirmation
  answers the three things a buyer wants after paying — Seats, flight, price paid
  — from the returned Booking plus the flight the page already loaded.
- **Booking history is two reads from two places**, exactly like the detail page:
  Bookings + Seats from Ordering, each flight from Catalog
  (`loadBookingHistory`, fetched once per flight, `null` for a flight Catalog no
  longer lists). The page (`web/src/routes/bookings-page.tsx`) is signed-in only,
  prompting sign-in otherwise, and renders loading / ready / empty / error.
- **Not live-verified this slice.** The authenticated confirm→booking→history
  paths are covered by integration tests through the whole app
  (`checkout.test.tsx`, `bookings.test.tsx`) and unit tests (`bookings.test.ts`);
  the Ordering-side `BookingReadServiceTests` need the Postgres container. A live
  authenticated pass against the compose stack is still owed — Docker was
  unavailable in the implementing session, the same constraint ticket 06/07 noted.
- **Tests:** frontend 90 (was 71) — `bookings.ts` unit (11), the confirm flow in
  `checkout.test.tsx` (3), and the history page through the app (5). Ordering
  gained `BookingReadServiceTests` (4) and a `BookingEndpointsTests` routing/auth
  guard (1, runs without a DB).
