# 06 — Catalog browsing and the (static) seat map

**What to build:** A visitor, signed in or not, can browse the flights running flash sales and open any flight to see its seat map with each Seat's current status. No live updating yet — refresh to see changes.

**Blocked by:** 03, 05.

**Status:** in-review

- [x] `IFlightCatalogService` with `ListFlights()` and `GetFlight(id)`.
- [x] `Catalog` consumes `SeatsHeld` / `SeatsReleased` / `SeatsConfirmed` from `Ordering` and maintains its seat-count projection.
- [x] `Ordering` never reads this projection when granting a Hold — its own tests pass with the projection absent or stale.
- [x] Catalog list page shows each flight's route, departure, FlashPrice, remaining seats, and time left in its sale window.
- [x] Flight detail page renders the seat map with every Seat as Available, Held, or Confirmed.
- [x] Sale state is visible and correct for upcoming, live, and ended flights.
- [x] Fully browsable while signed out.

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

### Implementation notes

- **Events:** `SeatsHeld` / `SeatsReleased` / `SeatsConfirmed` land in
  `FlashFlights.Contracts` (`SeatMovementEvents.cs`), carrying `FlightId`,
  `SeatIds`, and Ordering's `OccurredAt`. Ordering publishes them after each
  committed operation through a narrow `ISeatMovementNotifier` seam, so the
  Hold logic stays unaware of the bus and the concurrency tests need no broker.
  The bus adapter is best-effort: a broker hiccup can never fail a Hold that
  already committed.
- **Two reads, two places (by design):** the list page's remaining-seats comes
  from Catalog's `SeatCounts` projection (`FlightCatalogService`); the detail
  page's seat map comes live from Ordering (`ISeatMapService`, anonymous
  `GET /api/ordering/seats?flightId=`). The map self-heals a lapsed Hold before
  the counts catch up, exactly as the Comments above require.
- **Advisory-count limitation, accepted:** `SeatCountsProjector` dedups on
  Ordering's clock as a high-water mark. Two *distinct* operations on one Flight
  in the same clock tick would drop one delta with no self-heal. Left as a
  conscious trade — the counts are advisory and the authoritative view is the
  seat map — rather than adding a message-id inbox for a browse-only tally.
- **Tests:** backend 134 (Catalog projector + browse service on SQLite; Ordering
  seat map, notifier announcements, and an assembly-boundary test proving
  Ordering never references Catalog, on real PostgreSQL); frontend 61 (list and
  detail pages through the whole app, browsable signed out). Reviewed with
  `/code-review` — no hard standards violations; the duplicated newest-per-seat
  read was extracted to a shared `SeatLedger` so ADR-0001's tiebreak lives once.
