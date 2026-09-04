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
            .Select(id => (Id: id, Status: SeatStatusRules.StatusOf(LatestOn(latest, id), now)))
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

    private async Task<Dictionary<Guid, LatestMovement>> LatestMovementsAsync(
        IReadOnlyList<Guid> seatIds,
        CancellationToken cancellationToken)
    {
        var movements = await db.SeatMovements
            .Where(movement => seatIds.Contains(movement.SeatId))
            .Select(movement => new
            {
                movement.SeatId,
                movement.Type,
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

                    return new LatestMovement(newest.Type, newest.ExpiresAt);
                });
    }

    private static LatestMovement? LatestOn(IReadOnlyDictionary<Guid, LatestMovement> latest, Guid seatId) =>
        latest.TryGetValue(seatId, out var movement) ? movement : null;

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
}
