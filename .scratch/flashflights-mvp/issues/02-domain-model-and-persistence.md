# 02 — Domain model and persistence

**What to build:** The domain spine exists as real, migrated schema in each service's own datastore. After a fresh `docker-compose up`, every service has its tables with no manual migration step.

**Blocked by:** 01.

**Status:** ready-for-human

- [x] `Ordering` uses the **PostgreSQL** provider (per [ADR-0001](../../../docs/adr/0001-seat-movement-ledger.md) — row-level locking is required); `Catalog` and `Notifications` use SQLite.
- [x] `Catalog` schema: `Flight` (route, departure, FlashPrice, SaleStartsAt, SaleEndsAt) and its seat-count projection table.
- [x] `Ordering` schema: `Seat`, `SeatMovement` (`Held | Released | Confirmed`), `Hold`, `Booking`. `SeatMovement` is insert-only from the application's perspective.
- [x] **No stored `SeatStatus` column anywhere** — status is computed from movements (ADR-0001).
- [x] `Notifications` schema: `Watch`, `Notification`.
- [x] Identity/user store schema for the shared auth piece.
- [x] EF Core migrations exist per service and are applied automatically on startup.
- [x] Verified from a clean clone: `docker-compose up` produces every schema with correct tables, no manual step.

## Comments

### Implementation notes

**Four stores, four migrations, one shared runner.** Each service calls
`AddFlashFlightsDataStore<TContext>` once with its own provider and connection
string; that registers the context, a migrator, a readiness probe, and the
retrying startup service. The gateway calls it too — it hosts the Identity
store but takes no part in the bus, which is why datastore registration is now
separate from `AddFlashFlightsServiceDefaults` rather than folded into it.

**Readiness now means "migrated", not just "reachable".** Ticket 01's readiness
check answered "is the database up". With migrations in play that leaves a real
window — the container is running, PostgreSQL is answering, and the tables do
not exist yet — in which a service would have called itself ready and 500'd on
its first query. `DataStoreReadiness` latches when migrations finish, and the
health check refuses to go green before it. The connectivity half stays
un-latched, for the reason ticket 01 found: a latched flag keeps claiming
"reachable" after the database goes away.

**The hand-rolled ADO probes are gone.** `SqliteDataStoreProbe` and
`PostgresDataStoreProbe` are replaced by one generic `DbContextDataStoreProbe`
over the service's own context, so there is a single configured path to each
store rather than a second connection string quietly diverging from the one the
app actually uses. It still executes `SELECT 1` rather than merely opening —
that finding from ticket 01 stands.

**What is deliberately absent from the Ordering schema**, and is the design:

- *No Seat status column.* Status is derived from the Seat's movements.
- *No Hold status column.* Whether a Hold is live, confirmed, or expired
  follows from `ExpiresAt` plus whether a resolving movement exists, so an
  unresolved Hold past its TTL reads as expired without the sweep having run.
- *No Hold-to-Seat join table.* A Hold's Seats **are** its `Held` movements
  (CONTEXT.md defines it that way). A second list of them could only ever drift
  out of agreement with the ledger.

`OrderingSchemaTests` asserts each of these by name, so a later ticket adding
one fails a test that says why rather than silently reintroducing the mutable
state ADR-0001 exists to remove.

**The ledger is append-only in two layers.** `OrderingDbContext.SaveChanges`
rejects any `SeatMovement` entry in `Modified` or `Deleted` state and names the
offending seat; the guard runs before any SQL is generated, so its tests need no
database at all. But change tracking only sees work routed through
`SaveChanges` — `ExecuteUpdate`, `ExecuteDelete`, and raw SQL walk straight past
it — so migration `SeatMovementLedgerAppendOnly` adds a statement-level
PostgreSQL trigger that raises on any `UPDATE` or `DELETE` of the table.
ADR-0001 rests on this ledger being an auditable record of what actually
happened, so the rule belongs where nothing can route around it.

**Identity is keyed by `Guid`, not Identity's default `string`**, so the
`UserId` that Ordering and Notifications store is the same type on both sides of
the wire. It uses `IdentityUserContext` rather than `IdentityDbContext` — there
is no admin role and no roles at all, so the four role tables would only ever
have been empty.

**Verified against the real stack**, not just tests: `docker compose up` from
removed volumes produced all four schemas with no manual step — `Seats`,
`SeatMovements`, `Holds`, `Bookings` in PostgreSQL; `Flights` +
`FlightSeatCounts` and `Watches` + `Notifications` in their SQLite volumes;
`AspNetUsers` and friends (and no role tables) in the gateway's. Ordering
happened to start before PostgreSQL was accepting connections, so the retry
path was exercised for real — it migrated on attempt 2. A restart of all three
services re-reported ready without re-running or disturbing anything. All three
services answered `/health/ready` through the gateway's published port.

The trigger was checked against the live database by hand: inserting a movement
succeeds, and both `UPDATE "SeatMovements"` and `DELETE FROM "SeatMovements"`
are refused with the ledger message, leaving the row untouched. It has **no
automated test yet** — that needs a containerised PostgreSQL harness, which is
ticket 05's to build. Ticket 05 should add one when it does.

*Host note:* the host running this verification had Windows reserving TCP
8049–8148 for Hyper-V, so compose could not publish 8080. Nothing in the repo
was changed for it — the committed compose file keeps 8080, and the published
entrypoint was proven via a throwaway, uncommitted port override.

### Decisions a later ticket should know about

- **`Hold.PricePerSeat` is stamped at hold time**, and `Booking.PricePaid` at
  confirm, so a price change mid-checkout cannot alter what the buyer agreed
  to. Ticket 05 still has to decide *where Ordering gets that price from* —
  `CreateHold(flightId, seatIds, userId)` carries no price argument, and
  Ordering must not read Catalog's projection when granting a Hold. Either a
  Catalog lookup or Ordering-owned seeded pricing fits this schema unchanged.
- **`Seat.SeatNumber`** ("12A") is computed from `RowNumber` + `ColumnLetter`,
  not stored. Uniqueness is a constraint on the pair. (CONTEXT.md sanctions
  `SeatNumber` as an attribute name, just not as the entity's.)
- **`SeatMovement.HoldId` is NOT NULL** — every movement traces to the Hold that
  caused it. Ticket 11's seed therefore cannot simply mark a Seat as Held or
  Confirmed; it has to create the Hold (and, for Confirmed, the Booking) that
  the movement belongs to. That is the ledger being honest, but it is real seed
  work to plan for.
- **Ticket 06's seat map must read from Ordering, not Catalog.** Catalog stores
  no Seat rows at all — only counts — so per-Seat status has to come from
  Ordering. Correct per ADR-0001; the read path does not exist yet.
- **`Notification` has a unique `(UserId, FlightId)` index**, which makes a
  redelivered `FlightSaleStarted` idempotent. This holds only while a sale
  going live is the single Notification trigger (it is, per CONTEXT.md);
  widen it if ticket 09 ever adds a second.
- **`Watch` un-watching deletes the row** — there is no watch history, and a
  deleted Watch cannot be matched by a later `FlightSaleStarted`.
- **`FlashPrice` is a `decimal` on SQLite**, which EF stores as text. It must
  not be ordered or compared on in SQL — do it in memory. Ordering's PostgreSQL
  has real `numeric(10,2)`.
- The `_ping` endpoints from ticket 01 are still here; ticket 04 removes them.
