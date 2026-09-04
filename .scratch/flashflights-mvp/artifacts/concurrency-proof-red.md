# Concurrency proof — the red before the green

Ticket 05 requires the no-double-hold guarantee to be *demonstrated*, not
asserted: the naive, unlocked `CreateHold` is committed first, observed handing
the same Seat to every buyer at once, and only then fixed with a row lock. This
is the captured red output, for the README (ticket 12).

## The naive implementation

`HoldService.LockSeatsAsync` was a no-op — the grant ran inside a transaction but
took no row lock:

```csharp
private Task LockSeatsAsync(IReadOnlyList<Guid> seatIds, CancellationToken cancellationToken) =>
    Task.CompletedTask;
```

## The test, run against real PostgreSQL

`CreateHoldConcurrencyTests` fires 20 parallel `CreateHold` calls — each on its
own connection, all released together by a `Barrier` — at a single Seat, and
asserts exactly one wins.

```
[xUnit.net 00:00:17.61]     FlashFlights.Ordering.Tests.CreateHoldConcurrencyTests.Exactly_one_of_many_parallel_holds_on_the_same_seat_wins [FAIL]
  Failed FlashFlights.Ordering.Tests.CreateHoldConcurrencyTests.Exactly_one_of_many_parallel_holds_on_the_same_seat_wins [5 s]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 1
Actual:   20

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1
```

Every one of the 20 contenders won the same Seat: each opened its transaction,
read a Seat with no live movement, and appended `Held`. The enclosing
transaction did nothing to stop this — under READ COMMITTED the readers never saw
each other. That is the double-hold this project exists to make impossible.

## The fix

`LockSeatsAsync` locks the Seat rows for the life of the transaction:

```sql
SELECT "Id" FROM "Seats" WHERE "Id" = ANY(@seatIds) ORDER BY "Id" FOR UPDATE
```

The second and later contenders block on the lock until the first commits, then
re-read the now-current ledger, see the `Held` movement, and lose cleanly with a
409 conflict. With the lock in place the same test is green: one grant, nineteen
conflicts, one `Held` movement in the ledger.
