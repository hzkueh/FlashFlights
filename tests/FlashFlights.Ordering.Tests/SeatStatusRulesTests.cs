using FlashFlights.Ordering.Domain;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// The status derivation and the TTL boundary, tested with no database at all:
/// they are pure rules, and the whole design turns on them being decided in one
/// place so a reader and the sweep can never disagree about the same Hold
/// (ADR-0001, ticket 05).
/// </summary>
public class SeatStatusRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_seat_with_no_movements_is_available()
    {
        Assert.Equal(SeatStatus.Available, SeatStatusRules.StatusOf(null, Now));
    }

    [Fact]
    public void A_released_seat_is_available_again()
    {
        var released = new LatestMovement(SeatMovementType.Released, Now.AddMinutes(-1));

        Assert.Equal(SeatStatus.Available, SeatStatusRules.StatusOf(released, Now));
    }

    [Fact]
    public void A_confirmed_seat_stays_confirmed()
    {
        var confirmed = new LatestMovement(SeatMovementType.Confirmed, Now.AddMinutes(-1));

        Assert.Equal(SeatStatus.Confirmed, SeatStatusRules.StatusOf(confirmed, Now));
    }

    [Fact]
    public void A_held_seat_within_its_ttl_is_held()
    {
        var held = new LatestMovement(SeatMovementType.Held, Now.AddSeconds(1));

        Assert.Equal(SeatStatus.Held, SeatStatusRules.StatusOf(held, Now));
    }

    [Fact]
    public void A_held_seat_past_its_ttl_computes_back_to_available_without_a_release()
    {
        var expired = new LatestMovement(SeatMovementType.Held, Now.AddSeconds(-1));

        Assert.Equal(SeatStatus.Available, SeatStatusRules.StatusOf(expired, Now));
    }

    /// <summary>
    /// The boundary the ticket singles out: at the exact expiry instant the Hold
    /// is expired, not live. Deciding it here, once, is what keeps a Hold from
    /// being takeable to a reader and confirmable to its owner at the same tick.
    /// </summary>
    [Fact]
    public void At_the_exact_expiry_instant_a_held_seat_is_already_available()
    {
        var atBoundary = new LatestMovement(SeatMovementType.Held, Now);

        Assert.Equal(SeatStatus.Available, SeatStatusRules.StatusOf(atBoundary, Now));
    }

    [Theory]
    [InlineData(1, false)]  // expiry still one second ahead: live
    [InlineData(0, true)]   // expiry is exactly now: expired (inclusive boundary)
    [InlineData(-1, true)]  // expiry one second in the past: expired
    public void Has_expired_is_inclusive_at_the_boundary(int expiresOffsetSeconds, bool expected)
    {
        var expiresAt = Now.AddSeconds(expiresOffsetSeconds);

        Assert.Equal(expected, SeatStatusRules.HasExpired(expiresAt, Now));
    }
}
