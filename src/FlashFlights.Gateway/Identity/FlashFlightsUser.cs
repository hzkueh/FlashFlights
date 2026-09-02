using Microsoft.AspNetCore.Identity;

namespace FlashFlights.Gateway.Identity;

/// <summary>
/// An authenticated buyer — the only actor in the system (CONTEXT.md).
///
/// Keyed by Guid rather than Identity's default string so that the UserId every
/// other service stores is the same type on both sides of the wire, and a
/// mis-typed id fails to compile rather than at a database round trip.
/// </summary>
public sealed class FlashFlightsUser : IdentityUser<Guid>;
