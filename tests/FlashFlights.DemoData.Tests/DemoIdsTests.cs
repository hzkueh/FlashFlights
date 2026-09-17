using FlashFlights.DemoData;

namespace FlashFlights.DemoData.Tests;

/// <summary>
/// The derivation four services rely on to seed one coherent world without
/// talking to each other. Every shared id here is a cross-service reference and
/// never an FK, so nothing downstream would catch two services disagreeing —
/// which makes "the same name gives the same id" a property worth asserting
/// rather than assuming.
/// </summary>
public class DemoIdsTests
{
    [Fact]
    public void The_same_name_always_gives_the_same_id()
    {
        Assert.Equal(DemoIds.Flight("FF104"), DemoIds.Flight("FF104"));
        Assert.Equal(DemoIds.User("ada"), DemoIds.User("ada"));
        Assert.Equal(DemoIds.Seat("FF104", "12A"), DemoIds.Seat("FF104", "12A"));
    }

    /// <summary>
    /// Pinned rather than merely self-consistent: a change to the derivation
    /// would silently relabel every seeded row, so it has to be a change someone
    /// made on purpose.
    /// </summary>
    [Fact]
    public void The_derivation_is_fixed_rather_than_merely_stable_within_one_run()
    {
        Assert.Equal(Guid.Parse("ceb71e45-ce2a-8060-a62c-9081b05826b8"), DemoIds.Flight("FF104"));
    }

    [Fact]
    public void Different_names_give_different_ids()
    {
        Assert.NotEqual(DemoIds.Flight("FF104"), DemoIds.Flight("FF221"));
        Assert.NotEqual(DemoIds.Seat("FF104", "12A"), DemoIds.Seat("FF104", "12B"));
        Assert.NotEqual(DemoIds.Seat("FF104", "12A"), DemoIds.Seat("FF221", "12A"));
    }

    /// <summary>
    /// A Flight and a buyer could otherwise share a name and so share an id —
    /// which is why the kind is part of what is hashed.
    /// </summary>
    [Fact]
    public void A_name_used_for_two_different_things_gives_two_different_ids()
    {
        Assert.NotEqual(DemoIds.User("ada"), DemoIds.Flight("ada"));
        Assert.NotEqual(DemoIds.Watch("ada", "FF104"), DemoIds.Notification("ada", "FF104"));
    }

    /// <summary>
    /// Two Holds by one buyer on one Flight are told apart by the first Seat
    /// they claim — the demo world never lets two claims share a Seat, so that
    /// is enough.
    /// </summary>
    [Fact]
    public void Two_holds_by_the_same_buyer_on_the_same_flight_are_distinct()
    {
        Assert.NotEqual(
            DemoIds.Hold("FF104", DemoUsers.Ada.Id, "12A"),
            DemoIds.Hold("FF104", DemoUsers.Ada.Id, "14C"));
    }

    [Fact]
    public void The_result_is_a_well_formed_uuid_rather_than_sixteen_loose_bytes()
    {
        Span<byte> bytes = stackalloc byte[16];
        Assert.True(DemoIds.Flight("FF104").TryWriteBytes(bytes, bigEndian: true, out _));

        // Version 8 — implementation-defined, RFC 9562 §5.8.
        Assert.Equal(0x80, bytes[6] & 0xF0);

        // The RFC 9562 variant, in the top two bits of byte 8.
        Assert.Equal(0x80, bytes[8] & 0xC0);
    }
}
