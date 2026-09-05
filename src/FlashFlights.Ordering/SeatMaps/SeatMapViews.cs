using FlashFlights.Ordering.Domain;

namespace FlashFlights.Ordering.SeatMaps;

/// <summary>
/// One Seat as a browser sees it: its identity, the label a buyer reads, the
/// grid position the SPA lays it out by, and its computed
/// <see cref="SeatStatus"/> as of the moment the map was read.
///
/// The status is derived from the Seat's latest movement (ADR-0001), never a
/// stored column — an unresolved Held past its TTL reads as Available here
/// without the sweep having run, so this map self-heals the instant a Hold
/// lapses even while Catalog's counts still trail it.
/// </summary>
public sealed record SeatMapEntry(
    Guid SeatId,
    string SeatNumber,
    int RowNumber,
    string ColumnLetter,
    SeatStatus Status);

/// <summary>
/// The live seat map for one Flight — Ordering's authoritative answer to "which
/// Seats can I pick right now?", read straight from the ledger. Empty when the
/// Flight has no Seats here (an id Catalog knows but Ordering was never seeded
/// for), which the caller renders as an empty map rather than a 404.
/// </summary>
public sealed record FlightSeatMap(Guid FlightId, IReadOnlyList<SeatMapEntry> Seats);
