using System.Security.Cryptography;
using System.Text;

namespace FlashFlights.DemoData;

/// <summary>
/// Derives the demo world's identifiers from names rather than generating them.
///
/// <para>
/// This is what lets four services seed one coherent world without talking to
/// each other. Catalog's Flight, Ordering's Seats for it, and the Watch
/// Notifications holds on it all have to agree on a FlightId — and every one of
/// those is a cross-service reference, never an FK, so nothing would catch a
/// disagreement. Deriving the id from the flight number means each service
/// arrives at the same Guid on its own, in any order, without a shared table or
/// a startup handshake.
/// </para>
///
/// <para>
/// It is also what makes re-seeding recognisable: the same name yields the same
/// id on every run and on every machine, so "is this row already here?" is a
/// question a seeder can answer by looking, and a byte-identical demo world can
/// be described in a bug report.
/// </para>
///
/// <para>
/// The scheme is a name-based UUIDv8 (RFC 9562 §5.8, which reserves version 8
/// for exactly this kind of implementation-defined derivation): SHA-256 over a
/// fixed namespace and the name, truncated to 16 bytes with the version and
/// variant bits set. SHA-256 rather than the SHA-1 of a classic v5 — nothing
/// here needs SHA-1's compatibility, and it is not worth the explaining.
/// </para>
/// </summary>
public static class DemoIds
{
    /// <summary>
    /// The namespace every demo id is derived under. Its only job is to keep
    /// these ids from colliding with anything else name-derived; it is arbitrary
    /// and fixed forever, because changing it re-labels the whole demo world.
    /// </summary>
    private static readonly Guid Namespace = new("f1a5f11e-0000-4000-8000-000000000001");

    /// <summary>A demo buyer, keyed by their short name, e.g. "ada".</summary>
    public static Guid User(string userKey) => Derive("user", userKey);

    /// <summary>
    /// One of the other passengers on a Flight — somebody who bought a Seat and
    /// has no account, which is what nearly everyone on a real flight is.
    /// </summary>
    public static Guid Passenger(string flightNumber, int index) =>
        Derive("passenger", $"{flightNumber}/{index}");

    /// <summary>A demo Flight, keyed by its flight number, e.g. "FF104".</summary>
    public static Guid Flight(string flightNumber) => Derive("flight", flightNumber);

    /// <summary>One Seat on a Flight, keyed by the Flight and the seat label, e.g. "FF104"/"12A".</summary>
    public static Guid Seat(string flightNumber, string seatNumber) =>
        Derive("seat", $"{flightNumber}/{seatNumber}");

    /// <summary>
    /// A demo Hold, keyed by the Flight, the buyer, and the first Seat it claims
    /// — enough to tell two Holds by the same buyer on the same Flight apart,
    /// since no Seat is ever claimed twice.
    /// </summary>
    public static Guid Hold(string flightNumber, Guid buyerId, string firstSeatNumber) =>
        Derive("hold", $"{flightNumber}/{buyerId}/{firstSeatNumber}");

    /// <summary>The Booking a resolved demo Hold became — one per Hold, so keyed by it.</summary>
    public static Guid Booking(Guid holdId) => Derive("booking", holdId.ToString());

    /// <summary>One demo Seat movement: the Hold that posted it, the Seat, and which of the three it is.</summary>
    public static Guid SeatMovement(Guid holdId, Guid seatId, string movementType) =>
        Derive("movement", $"{holdId}/{seatId}/{movementType}");

    /// <summary>A demo Watch — one per (buyer, Flight), which the store also enforces.</summary>
    public static Guid Watch(string userKey, string flightNumber) =>
        Derive("watch", $"{userKey}/{flightNumber}");

    /// <summary>A demo Notification — one per (buyer, Flight), which the store also enforces.</summary>
    public static Guid Notification(string userKey, string flightNumber) =>
        Derive("notification", $"{userKey}/{flightNumber}");

    private static Guid Derive(string kind, string name)
    {
        // The kind is part of the hashed input rather than a separate namespace
        // per kind, so a flight called "ada" and a buyer called "ada" cannot
        // collide.
        var input = Encoding.UTF8.GetBytes($"{Namespace:D}:{kind}:{name}");

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, hash);

        var id = hash[..16];

        // Version 8 (implementation-defined) in the high nibble of byte 6, and
        // the RFC 9562 variant in the top two bits of byte 8. Without these the
        // result is 16 random-looking bytes rather than a well-formed UUID, and
        // tooling that reads a version would be misled.
        id[6] = (byte)((id[6] & 0x0F) | 0x80);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);

        return new Guid(id, bigEndian: true);
    }
}
