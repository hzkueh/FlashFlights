# Seat status is derived from an append-only, per-seat SeatMovement ledger

A Seat's status is never stored as an authoritative mutable field. Instead
every change is recorded as an append-only **SeatMovement** (Held, Released,
or Confirmed, each referencing a SeatId and timestamp), and SeatStatus is
derived from the latest movement for that seat, computed inside the same
row-locked transaction that grants a new Hold. We chose this over a mutable
per-seat status column with optimistic concurrency because the Ordering
service must guarantee two buyers can never simultaneously hold or confirm
the same seat under real concurrent load — the actual point of this project
— and an append-only ledger makes that guarantee auditable and provable, at
the cost of more write and read logic than a single `UPDATE`.

This is a direct callback to RestaurantInventory's StockMovement ledger
(see its [ADR-0001](../../../RestaurantInventory/docs/adr/0001-stockmovement-ledger.md)),
now proving the same pattern holds under real concurrency across service
boundaries rather than within a single process.

## Consequences

- SeatStatus is computed on read, never cached as a stored column, so a
  stale cache can never claim a seat is takeable when it isn't.
- An unresolved Held past its TTL is treated as Available again without a
  background job being required for correctness — the cleanup job posts the
  compensating Released record for auditability.
- That cleanup job is nonetheless load-bearing for browsing. Catalog's
  SeatCounts learn of a silent expiry only from the Released movement the
  sweep posts, so until it runs the counts and the seat map disagree about the
  same Flight — the map reads live from Ordering and self-heals, the counts do
  not. The sweep's interval is therefore bounded by how stale the counts may
  be, not by audit latency alone.
- Catalog's browsing view is an eventually-consistent projection over these
  events; Ordering never consults it when actually granting a Hold.
- Ordering's datastore must support real row-level locking, so it is
  PostgreSQL rather than SQLite. SQLite serialises all writers behind a
  single database-level lock — it would appear to uphold this guarantee
  while proving nothing about it, since the race could never occur to be
  caught. Catalog and Notifications carry no such constraint.
