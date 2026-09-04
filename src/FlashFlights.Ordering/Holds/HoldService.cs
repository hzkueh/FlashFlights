using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlashFlights.Ordering.Holds;

/// <summary>
/// Grants Holds over the SeatMovement ledger. Every Seat's status is computed
/// from its movements inside the transaction that grants the Hold, never read
/// from a stored column (ADR-0001), and the grant is all-or-nothing: the loser
/// of a race for any one Seat gets a clean conflict and no movements are
/// written.
/// </summary>
public sealed class HoldService(
    OrderingDbContext db,
    TimeProvider clock,
    IOptions<HoldOptions> options) : IHoldService
{
    /// <summary>The field a malformed request's seat errors are keyed by — the SPA's seat picker owns it.</summary>
    public const string SeatIdsField = "seatIds";

    /// <summary>The field a malformed price is keyed by, matching the request body's own name.</summary>
    public const string PricePerSeatField = "pricePerSeat";

    private readonly TimeSpan _ttl = options.Value.Ttl;

    public async Task<CreateHoldResult> CreateHoldAsync(
        CreateHoldRequest request,
        CancellationToken cancellationToken = default)
    {
        // Malformed before the ledger is touched (400, not 409): a shape error is
        // the caller's to fix, and retrying the same request cannot help.
        if (ValidateRequest(request) is { } shapeErrors)
        {
            return new CreateHoldResult.Malformed(shapeErrors);
        }

        var seats = await db.Seats
            .Where(seat => request.SeatIds.Contains(seat.Id))
            .ToDictionaryAsync(seat => seat.Id, cancellationToken);

        // Matching each Seat against the supplied FlightId is the strongest
        // statement Ordering can make from its own data — it owns no Flight to
        // check existence against — and it is what stops a caller holding Seats
        // under the wrong Flight. A missing Seat and a Seat of another Flight are
        // the same malformed request from here.
        var notOfFlight = request.SeatIds
            .Where(id => !seats.TryGetValue(id, out var seat) || seat.FlightId != request.FlightId)
            .ToArray();

        if (notOfFlight.Length > 0)
        {
            return new CreateHoldResult.Malformed(SeatsNotOfFlight(notOfFlight));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await LockSeatsAsync(request.SeatIds, cancellationToken);

        var now = clock.GetUtcNow();
        var latest = await LatestMovementsAsync(request.SeatIds, cancellationToken);

        var conflicts = request.SeatIds
            .Select(id => (Id: id, Status: SeatStatusRules.StatusOf(StatusInputOn(latest, id), now)))
            .Where(seat => seat.Status != SeatStatus.Available)
            .Select(seat => new ConflictingSeat(seat.Id, seats[seat.Id].SeatNumber, seat.Status.ToString()))
            .ToArray();

        if (conflicts.Length > 0)
        {
            // Nothing was appended, so a rollback is only tidiness — but it keeps
            // "no partial success" a property of the code and not of luck.
            await transaction.RollbackAsync(cancellationToken);
            return new CreateHoldResult.Conflict(conflicts);
        }

        var hold = new Hold
        {
            Id = Guid.CreateVersion7(),
            FlightId = request.FlightId,
            UserId = request.UserId,
            CreatedAt = now,
            ExpiresAt = now + _ttl,
            PricePerSeat = request.PricePerSeat,
        };

        db.Holds.Add(hold);
        foreach (var seatId in request.SeatIds)
        {
            db.SeatMovements.Add(new SeatMovement
            {
                Id = Guid.CreateVersion7(),
                SeatId = seatId,
                HoldId = hold.Id,
                Type = SeatMovementType.Held,
                OccurredAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CreateHoldResult.Granted(ToView(hold, request.SeatIds, seats));
    }

    public async Task<ConfirmHoldResult> ConfirmHoldAsync(
        Guid holdId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var hold = await db.Holds.FirstOrDefaultAsync(h => h.Id == holdId, cancellationToken);

        // A Hold that does not exist and one that is not the caller's are a single
        // answer: a buyer may only confirm their own Hold, and distinguishing the
        // two would leak which Hold ids are real.
        if (hold is null || hold.UserId != userId)
        {
            return new ConfirmHoldResult.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Lock exactly the Hold's Seats so a concurrent confirm, or the expiry
        // sweep, serialises behind us rather than deciding this Hold's fate
        // underneath us. It is the same lock CreateHold takes, which is what keeps
        // confirm and grant from racing across the same Seat.
        var heldSeatIds = await HeldSeatIdsAsync(holdId, cancellationToken);

        await LockSeatsAsync(heldSeatIds, cancellationToken);

        // One Booking per Hold is a database constraint; checking it under the
        // lock turns the second confirm of a race from a unique-violation into a
        // clean AlreadyConfirmed. A resolved Hold cannot be re-confirmed.
        if (await HasBookingAsync(holdId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ConfirmHoldResult.AlreadyConfirmed();
        }

        // The same clock and the same boundary the read side uses to decide a Seat
        // is takeable again (SeatStatusRules.HasExpired): a Hold that has reached
        // its TTL reads as expired to a browser, so it must be too late to confirm
        // here too — never live to a confirm while expired to a reader.
        var now = clock.GetUtcNow();

        if (SeatStatusRules.HasExpired(hold.ExpiresAt, now))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ConfirmHoldResult.Expired();
        }

        // SeatNumber is computed, not a column, so the whole Seat is loaded and
        // the label read in memory — the same shape CreateHold uses.
        var seats = await db.Seats
            .Where(seat => heldSeatIds.Contains(seat.Id))
            .ToDictionaryAsync(seat => seat.Id, cancellationToken);

        // The simulated payment always succeeds instantly (CONTEXT.md); the price
        // paid is the FlashPrice frozen onto the Hold times the Seats confirmed,
        // so a later Catalog price change cannot alter what the buyer agreed to.
        var booking = new Booking
        {
            Id = Guid.CreateVersion7(),
            HoldId = hold.Id,
            UserId = hold.UserId,
            ConfirmedAt = now,
            PricePaid = hold.PricePerSeat * heldSeatIds.Length,
        };

        db.Bookings.Add(booking);
        foreach (var seatId in heldSeatIds)
        {
            db.SeatMovements.Add(new SeatMovement
            {
                Id = Guid.CreateVersion7(),
                SeatId = seatId,
                HoldId = hold.Id,
                Type = SeatMovementType.Confirmed,
                OccurredAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ConfirmHoldResult.Confirmed(ToBookingView(booking, heldSeatIds, seats));
    }

    public async Task<ExpireHoldsResult> ExpireHoldsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        // Candidate Holds: past their TTL, never confirmed, and not yet released.
        // A Hold's movements are uniform — all Held, or Held+Confirmed (it has a
        // Booking), or Held+Released — so "no Booking and no Released movement" is
        // exactly the set the sweep still owes a compensating Released. Confirming
        // the boundary and the per-Seat state happens under the lock, in
        // ReleaseExpiredHoldAsync; this query only narrows the work.
        var expiredHoldIds = await db.Holds
            .Where(hold => hold.ExpiresAt <= now)
            .Where(hold => hold.Booking == null)
            .Where(hold => hold.Movements.All(movement => movement.Type != SeatMovementType.Released))
            .Select(hold => hold.Id)
            .ToArrayAsync(cancellationToken);

        var holdsExpired = 0;
        var seatsReleased = 0;

        // One transaction per Hold, not one over the whole sweep: the lock stays
        // narrow and a confirm racing one Hold is never blocked behind the release
        // of an unrelated one.
        foreach (var holdId in expiredHoldIds)
        {
            var released = await ReleaseExpiredHoldAsync(holdId, cancellationToken);
            if (released > 0)
            {
                holdsExpired++;
                seatsReleased += released;
            }
        }

        return new ExpireHoldsResult(holdsExpired, seatsReleased);
    }

    /// <summary>
    /// Releases one expired Hold's still-held Seats under the row lock, or does
    /// nothing if a confirm won the race or the Hold turns out not to be expired
    /// on the shared boundary. Returns the number of Seats released.
    /// </summary>
    private async Task<int> ReleaseExpiredHoldAsync(Guid holdId, CancellationToken cancellationToken)
    {
        var hold = await db.Holds.FirstOrDefaultAsync(h => h.Id == holdId, cancellationToken);
        if (hold is null)
        {
            return 0;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var heldSeatIds = await HeldSeatIdsAsync(holdId, cancellationToken);

        await LockSeatsAsync(heldSeatIds, cancellationToken);

        // The one boundary every path shares (SeatStatusRules.HasExpired): the
        // sweep must not release a Hold a confirm could still win. Re-checked here
        // under the lock so a confirm that committed since the candidate query
        // cannot be undone.
        var now = clock.GetUtcNow();
        if (!SeatStatusRules.HasExpired(hold.ExpiresAt, now))
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        if (await HasBookingAsync(holdId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        // Only release a Seat whose latest movement is still this Hold's Held. A
        // Seat that read-side expiry let a newer Hold take has moved on, and a
        // Released posted for it would wrongly free the newer Hold's Seat. This is
        // also what makes the sweep idempotent: a Seat already Released is no
        // longer its own latest-Held, so a second run skips it.
        var latest = await LatestMovementsAsync(heldSeatIds, cancellationToken);

        var released = 0;
        foreach (var seatId in heldSeatIds)
        {
            if (latest.TryGetValue(seatId, out var movement)
                && movement.Type == SeatMovementType.Held
                && movement.HoldId == holdId)
            {
                db.SeatMovements.Add(new SeatMovement
                {
                    Id = Guid.CreateVersion7(),
                    SeatId = seatId,
                    HoldId = holdId,
                    Type = SeatMovementType.Released,
                    OccurredAt = now,
                });
                released++;
            }
        }

        if (released > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return released;
    }

    /// <summary>
    /// Takes a row lock over the requested Seats for the life of the transaction
    /// — the one line the whole no-double-hold guarantee rests on. A concurrent
    /// CreateHold for any of the same Seats blocks here until this transaction
    /// commits or rolls back, then re-reads the now-current ledger and sees the
    /// Held movement this one wrote, so it loses cleanly instead of granting a
    /// second Hold. The enclosing transaction alone does not achieve this: under
    /// READ COMMITTED, unlocked readers never see each other (this is exactly
    /// what the concurrency test catches when the lock is removed).
    ///
    /// The rows are locked in a fixed order (by Id) so two requests for
    /// overlapping Seat sets cannot deadlock by grabbing them in opposite
    /// orders. The SELECT's rows are discarded — it is run only for its locks.
    /// </summary>
    private Task LockSeatsAsync(IReadOnlyList<Guid> seatIds, CancellationToken cancellationToken)
    {
        var ids = seatIds.ToArray();

        return db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "Id" FROM "Seats" WHERE "Id" = ANY({ids}) ORDER BY "Id" FOR UPDATE""",
            cancellationToken);
    }

    /// <summary>
    /// A Hold's Seats <em>are</em> its Held movements (CONTEXT.md) — there is no
    /// seat list to read. Both confirm and the sweep start from this, and lock
    /// exactly these rows.
    /// </summary>
    private Task<Guid[]> HeldSeatIdsAsync(Guid holdId, CancellationToken cancellationToken) =>
        db.SeatMovements
            .Where(movement => movement.HoldId == holdId && movement.Type == SeatMovementType.Held)
            .Select(movement => movement.SeatId)
            .ToArrayAsync(cancellationToken);

    /// <summary>
    /// Whether the Hold has already resolved into a Booking. One Booking per Hold
    /// is a database constraint, so this read under the lock is what turns a
    /// re-confirm, or a confirm/sweep race, into a clean outcome rather than a
    /// unique-violation.
    /// </summary>
    private Task<bool> HasBookingAsync(Guid holdId, CancellationToken cancellationToken) =>
        db.Bookings.AnyAsync(booking => booking.HoldId == holdId, cancellationToken);

    /// <summary>
    /// The newest movement on each of the given Seats — the ledger's central read
    /// (ADR-0001) — reduced to everything its two callers need between them: the
    /// Hold's expiry for the status rules, and the type and owning Hold for the
    /// sweep's "is this Hold's Held still the Seat's latest?" decision.
    /// </summary>
    private async Task<Dictionary<Guid, SeatLedgerHead>> LatestMovementsAsync(
        IReadOnlyList<Guid> seatIds,
        CancellationToken cancellationToken)
    {
        var movements = await db.SeatMovements
            .Where(movement => seatIds.Contains(movement.SeatId))
            .Select(movement => new
            {
                movement.SeatId,
                movement.Type,
                movement.HoldId,
                movement.OccurredAt,
                movement.Id,
                ExpiresAt = movement.Hold!.ExpiresAt,
            })
            .ToListAsync(cancellationToken);

        // Newest movement per Seat decides its status; Id breaks a tie on the
        // timestamp so the winner is deterministic even when a Seat gains two
        // movements in the same instant.
        return movements
            .GroupBy(movement => movement.SeatId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var newest = group
                        .OrderByDescending(movement => movement.OccurredAt)
                        .ThenByDescending(movement => movement.Id)
                        .First();

                    return new SeatLedgerHead(newest.Type, newest.HoldId, newest.ExpiresAt);
                });
    }

    /// <summary>The status-rule view of one Seat's latest movement — just the fields <see cref="SeatStatusRules"/> reads.</summary>
    private static LatestMovement? StatusInputOn(IReadOnlyDictionary<Guid, SeatLedgerHead> heads, Guid seatId) =>
        heads.TryGetValue(seatId, out var head) ? new LatestMovement(head.Type, head.HoldExpiresAt) : null;

    private static IReadOnlyDictionary<string, string[]>? ValidateRequest(CreateHoldRequest request)
    {
        var errors = new Dictionary<string, List<string>>();

        if (request.SeatIds.Count == 0)
        {
            AddError(errors, SeatIdsField, "Select at least one seat to hold.");
        }

        if (request.SeatIds.Distinct().Count() != request.SeatIds.Count)
        {
            AddError(errors, SeatIdsField, "The same seat was selected more than once.");
        }

        // The price is caller-supplied and taken on trust, but a negative one is
        // not a difference of opinion about the FlashPrice — it is malformed, and
        // it would otherwise freeze onto the Hold and become a negative Booking
        // total at confirm.
        if (request.PricePerSeat < 0)
        {
            AddError(errors, PricePerSeatField, "Price per seat cannot be negative.");
        }

        return errors.Count == 0
            ? null
            : errors.ToDictionary(field => field.Key, field => field.Value.ToArray());
    }

    private static void AddError(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var messages))
        {
            errors[field] = messages = [];
        }

        messages.Add(message);
    }

    private static IReadOnlyDictionary<string, string[]> SeatsNotOfFlight(IReadOnlyCollection<Guid> seatIds) =>
        new Dictionary<string, string[]>
        {
            [SeatIdsField] = [$"These seats are not part of the flight: {string.Join(", ", seatIds)}."],
        };

    private static HoldView ToView(Hold hold, IReadOnlyList<Guid> seatIds, IReadOnlyDictionary<Guid, Seat> seats) =>
        new(
            hold.Id,
            hold.FlightId,
            hold.UserId,
            hold.ExpiresAt,
            hold.PricePerSeat,
            [.. seatIds.Select(id => new HeldSeatView(id, seats[id].SeatNumber))]);

    private static BookingView ToBookingView(
        Booking booking,
        IReadOnlyList<Guid> seatIds,
        IReadOnlyDictionary<Guid, Seat> seats) =>
        new(
            booking.Id,
            booking.HoldId,
            booking.UserId,
            booking.ConfirmedAt,
            booking.PricePaid,
            [.. seatIds.Select(id => new BookedSeatView(id, seats[id].SeatNumber))]);

    /// <summary>
    /// One Seat's latest movement, reduced to what the callers of
    /// <see cref="LatestMovementsAsync"/> decide on: its type, the Hold that posted
    /// it, and that Hold's expiry.
    /// </summary>
    private readonly record struct SeatLedgerHead(SeatMovementType Type, Guid HoldId, DateTimeOffset HoldExpiresAt);
}
