using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Gateway.Identity;

/// <summary>
/// The one shared user store, hosted alongside the gateway rather than as a
/// fourth microservice: Catalog, Ordering, and Notifications validate JWTs
/// against shared signing config and keep no user store of their own.
///
/// <c>IdentityUserContext</c>, not <c>IdentityDbContext</c> — there is no admin
/// role and no roles at all, so the four role tables would only ever be empty.
/// SQLite: nothing here needs row-level locking (ADR-0001).
/// </summary>
public sealed class FlashFlightsIdentityDbContext(DbContextOptions<FlashFlightsIdentityDbContext> options)
    : IdentityUserContext<FlashFlightsUser, Guid>(options);
