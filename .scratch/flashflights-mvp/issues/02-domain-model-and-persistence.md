# 02 — Domain model and persistence

**What to build:** The domain spine exists as real, migrated schema in each service's own datastore. After a fresh `docker-compose up`, every service has its tables with no manual migration step.

**Blocked by:** 01.

**Status:** ready-for-agent

- [ ] `Ordering` uses the **PostgreSQL** provider (per [ADR-0001](../../../docs/adr/0001-seat-movement-ledger.md) — row-level locking is required); `Catalog` and `Notifications` use SQLite.
- [ ] `Catalog` schema: `Flight` (route, departure, FlashPrice, SaleStartsAt, SaleEndsAt) and its seat-count projection table.
- [ ] `Ordering` schema: `Seat`, `SeatMovement` (`Held | Released | Confirmed`), `Hold`, `Booking`. `SeatMovement` is insert-only from the application's perspective.
- [ ] **No stored `SeatStatus` column anywhere** — status is computed from movements (ADR-0001).
- [ ] `Notifications` schema: `Watch`, `Notification`.
- [ ] Identity/user store schema for the shared auth piece.
- [ ] EF Core migrations exist per service and are applied automatically on startup.
- [ ] Verified from a clean clone: `docker-compose up` produces every schema with correct tables, no manual step.
