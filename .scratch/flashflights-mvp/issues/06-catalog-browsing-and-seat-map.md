# 06 — Catalog browsing and the (static) seat map

**What to build:** A visitor, signed in or not, can browse the flights running flash sales and open any flight to see its seat map with each Seat's current status. No live updating yet — refresh to see changes.

**Blocked by:** 03, 05.

**Status:** ready-for-agent

- [ ] `IFlightCatalogService` with `ListFlights()` and `GetFlight(id)`.
- [ ] `Catalog` consumes `SeatsHeld` / `SeatsReleased` / `SeatsConfirmed` from `Ordering` and maintains its seat-count projection.
- [ ] `Ordering` never reads this projection when granting a Hold — its own tests pass with the projection absent or stale.
- [ ] Catalog list page shows each flight's route, departure, FlashPrice, remaining seats, and time left in its sale window.
- [ ] Flight detail page renders the seat map with every Seat as Available, Held, or Confirmed.
- [ ] Sale state is visible and correct for upcoming, live, and ended flights.
- [ ] Fully browsable while signed out.
