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

## Comments

### The counts and the seat map can disagree — by design

The "seat-count projection" above is **SeatCounts** in `CONTEXT.md`. Its entry
is worth reading before building this ticket: the counts are advisory and can
disagree with the SeatStatus of the very Seats they count.

Concretely, this ticket's two pages read from different places. The list page's
remaining-seats figure comes from Catalog's SeatCounts; the seat map comes from
Ordering (Catalog stores no Seat rows — see ticket 02). A Hold that expires
silently is Available in Ordering immediately, but reaches SeatCounts only via
the `Released` movement the sweep posts, so for up to one sweep interval the
list can say "3 left" while that flight's own detail page renders 4 Available
seats.

Ticket 05 bounds this by requiring the sweep to run well under the Hold TTL,
and ADR-0001 now records the sweep as load-bearing for browsing rather than
auditability alone. Don't close the remaining window by having Catalog compute
expiry itself — it holds no Hold TTLs — or by having the list page read
Ordering per flight, which would put the browsing path on the hot booking path
that ADR-0001 keeps it off.
