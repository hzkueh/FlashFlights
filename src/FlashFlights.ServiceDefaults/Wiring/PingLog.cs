using FlashFlights.Contracts;

namespace FlashFlights.ServiceDefaults.Wiring;

/// <summary>
/// Records <see cref="PingSent"/> messages this service consumed, so the bus
/// wiring is observable over HTTP rather than only in container logs.
/// Part of the ticket-01 wiring probe — delete with <see cref="PingSent"/>.
/// </summary>
public interface IPingLog
{
    IReadOnlyList<PingSent> Received { get; }

    void Record(PingSent ping);
}

/// <summary>In-memory and deliberately lossy: it keeps only the most recent pings.</summary>
public sealed class InMemoryPingLog : IPingLog
{
    private const int Capacity = 50;

    private readonly Lock _gate = new();
    private readonly List<PingSent> _received = [];

    public IReadOnlyList<PingSent> Received
    {
        get
        {
            lock (_gate)
            {
                return _received.ToArray();
            }
        }
    }

    public void Record(PingSent ping)
    {
        lock (_gate)
        {
            _received.Add(ping);
            if (_received.Count > Capacity)
            {
                _received.RemoveAt(0);
            }
        }
    }
}
