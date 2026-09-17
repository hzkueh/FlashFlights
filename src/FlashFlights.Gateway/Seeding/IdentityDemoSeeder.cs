using FlashFlights.DemoData;
using FlashFlights.Gateway.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Gateway.Seeding;

/// <summary>
/// The gateway's share of the demo world: the buyers whose bookings, watches,
/// and inbox the other three services have already seeded.
///
/// <para>
/// Created through <see cref="UserManager{TUser}"/> rather than by inserting
/// rows, so a demo buyer is hashed, stamped, and normalised by exactly the code
/// that registers a real one — an account that cannot sign in would be worse
/// than no account at all, and only the real path proves it can.
/// </para>
/// </summary>
public sealed class IdentityDemoSeeder(
    FlashFlightsIdentityDbContext db,
    UserManager<FlashFlightsUser> users) : IDemoSeeder
{
    public async Task<bool> SeedAsync(DemoWorld world, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);

        // Any User at all: a store someone has already registered against is not
        // one to add public-password accounts to.
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return false;
        }

        // Explicit, because UserManager saves one User at a time. Without it a
        // failure on the last buyer would leave the first few behind, and the
        // next attempt would find a non-empty store and take it for a finished
        // job.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var buyer in world.Users)
        {
            // The derived id, not the Version 7 one registration mints. Every
            // other service has already written Holds, Watches, and Notifications
            // against this exact Guid, and none of them could be told a different
            // one — they are cross-service references, never FKs. The clustering
            // argument for v7 is about a growing user table, which four seeded
            // rows are not.
            var user = new FlashFlightsUser
            {
                Id = buyer.Id,
                UserName = buyer.Email,
                Email = buyer.Email,
            };

            var result = await users.CreateAsync(user, DemoUsers.Password);

            if (!result.Succeeded)
            {
                // Thrown rather than logged: a half-seeded set of buyers is a
                // demo where one account has a booking history and the next
                // cannot sign in, which is harder to diagnose than a service
                // that plainly refuses to come up.
                throw new InvalidOperationException(
                    $"Could not seed demo user {buyer.Email}: "
                    + string.Join("; ", result.Errors.Select(error => error.Description)));
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}
