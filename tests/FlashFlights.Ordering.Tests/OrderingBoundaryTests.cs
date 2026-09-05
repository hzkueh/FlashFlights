using System.Reflection;
using FlashFlights.Ordering.Holds;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// The architectural half of ADR-0001's guarantee: Ordering grants Holds from
/// its own ledger and never consults Catalog's advisory projection. Proven here
/// as a boundary the compiler enforces — Ordering cannot read a projection it
/// cannot even reference — which is why every Hold test in this assembly runs
/// with the projection absent and none is the worse for it.
/// </summary>
public class OrderingBoundaryTests
{
    [Fact]
    public void Ordering_does_not_reference_catalog()
    {
        var referenced = typeof(HoldService).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name);

        Assert.DoesNotContain("FlashFlights.Catalog", referenced);
    }
}
