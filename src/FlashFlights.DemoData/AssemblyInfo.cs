using System.Runtime.CompilerServices;

// The adapter that decides whether the seed runs at all is internal — it is
// wiring, not API. Its tests reach it directly rather than through a host,
// because what needs proving is the decision, not the container.
[assembly: InternalsVisibleTo("FlashFlights.DemoData.Tests")]
