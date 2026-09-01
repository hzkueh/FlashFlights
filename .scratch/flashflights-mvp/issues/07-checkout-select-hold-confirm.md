# 07 — Checkout: select seats, hold, confirm, book

**What to build:** A signed-in User picks Seats on the map, requests a Hold, watches a countdown, confirms through the simulated payment step, and lands on a booking confirmation — and can see their past Bookings.

**Blocked by:** 04, 06.

**Status:** ready-for-agent

- [ ] Signed-in User can select one or more Available Seats on the map.
- [ ] Requesting a Hold calls `Ordering` and reserves exactly those Seats.
- [ ] A losing race is surfaced clearly — the User is told which Seats were taken and returned to a usable state, never shown a false success.
- [ ] Live countdown of the Hold's remaining time; expiry releases the Seats and tells the User.
- [ ] Confirm step completes the simulated payment and produces a Booking.
- [ ] Booking confirmation screen shows Seats, flight, and price paid.
- [ ] Booking history page lists the User's past Bookings.
- [ ] Signed-out visitors are prompted to sign in when they try to hold Seats.
