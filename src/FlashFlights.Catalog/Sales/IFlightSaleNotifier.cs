using FlashFlights.Contracts;

namespace FlashFlights.Catalog.Sales;

/// <summary>
/// How <see cref="SaleStartAnnouncer"/> tells the rest of the system a flash
/// sale has opened, without knowing there is a bus. The same shape Ordering's
/// <c>ISeatMovementNotifier</c> gives the Hold logic, so the announcer is
/// testable without a broker.
///
/// <para>
/// It carries the whole <see cref="FlightSaleStarted"/> rather than the
/// primitives Ordering's notifier takes, because there is no shaping to do here:
/// one Flight crossing its window is exactly one message, and splitting it into
/// six arguments only to reassemble it would put the same field order in two
/// places.
/// </para>
///
/// <para>
/// Unlike the seat-movement publish, this one does <em>not</em> swallow
/// failures. A lost movement event leaves Catalog's advisory counts briefly
/// stale and the next movement carries them forward; a lost announcement is the
/// alert every watcher asked for, gone, with nothing behind it to heal — so a
/// failure must surface, leave the Flight unannounced, and be retried on the
/// next tick.
/// </para>
/// </summary>
public interface IFlightSaleNotifier
{
    /// <summary>
    /// Announces that a Flight's flash sale has opened. Throws if the
    /// announcement could not be published.
    /// </summary>
    Task SaleStartedAsync(FlightSaleStarted announcement, CancellationToken cancellationToken = default);
}
