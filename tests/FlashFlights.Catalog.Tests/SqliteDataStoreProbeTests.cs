using FlashFlights.Catalog.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace FlashFlights.Catalog.Tests;

public class SqliteDataStoreProbeTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"flashflights-catalog-{Guid.NewGuid():N}");

    [Fact]
    public async Task Connects_by_creating_the_database_file_when_the_volume_is_empty()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "catalog.db");
        var probe = BuildProbe($"Data Source={path}");

        await probe.ConnectAsync(CancellationToken.None);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Fails_when_the_datastore_cannot_be_reached()
    {
        // The directory is deliberately never created, so SQLite cannot open the file.
        var probe = BuildProbe($"Data Source={Path.Combine(_directory, "catalog.db")}");

        await Assert.ThrowsAnyAsync<Exception>(
            () => probe.ConnectAsync(CancellationToken.None));
    }

    [Fact]
    public void Refuses_to_start_without_a_configured_connection_string()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => new SqliteDataStoreProbe(configuration));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        // The probe's connection returns to the SQLite pool, which keeps the
        // file handle open until the pool is cleared.
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static SqliteDataStoreProbe BuildProbe(string connectionString) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CatalogDb"] = connectionString,
            })
            .Build());
}
