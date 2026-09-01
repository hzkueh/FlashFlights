# 05 — Ordering core: Hold / Confirm / Expire, with the concurrency proof

**What to build:** The heart of the system. `Ordering` can grant a Hold on a set of specific Seats, confirm it into a Booking, and expire it back to Available — and it is *proven*, by a test that fires genuinely parallel requests at the same Seat, that two buyers can never both win it.

**Blocked by:** 02.

**Status:** ready-for-agent

- [ ] `IHoldService` with `CreateHold(flightId, seatIds, userId)`, `ConfirmHold(holdId)`, and an `ExpireHolds` sweep, reachable over HTTP through the gateway.
- [ ] Accepts a caller-supplied user id — does **not** depend on ticket 04, so it can be built in parallel with a stub id.
- [ ] `CreateHold` appends `Held` SeatMovements inside a single row-locked transaction; SeatStatus is computed from movements, never read from a stored column.
- [ ] `CreateHold` on an already-Held or Confirmed Seat fails cleanly and names the conflicting Seats — never a partial success.
- [ ] `ConfirmHold` appends `Confirmed` movements and produces a `Booking` with the Seats, User, and price paid; a simulated payment step always succeeds instantly.
- [ ] A resolved Hold cannot be re-confirmed or re-held.
- [ ] An unresolved Hold past its TTL (~2 min) computes back to Available on read **without** requiring the sweep to have run; the sweep posts the compensating `Released` movements for auditability.
- [ ] Read-side expiry and the sweep read the **same clock source** and agree on the boundary — a Hold is never simultaneously "expired" to a reader and "live" to a confirm. Boundary case is covered by a test.
- [ ] Sequential tests: hold → confirm → Booking; hold → expire → Seats Available again; conflict paths above.
- [ ] Concurrency test fires N parallel `CreateHold` calls at the same Seat and asserts exactly one wins and the rest get a clean conflict.
- [ ] Concurrency tests run against a **real containerised PostgreSQL**, never an in-memory provider or SQLite.
- [ ] The concurrency test is **demonstrated red first**: the naive unlocked implementation is committed, observed producing a double-hold, then fixed. Capture the red output — it goes in the README (ticket 12).
