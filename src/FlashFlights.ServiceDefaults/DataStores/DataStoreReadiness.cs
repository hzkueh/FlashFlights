namespace FlashFlights.ServiceDefaults.DataStores;

/// <summary>
/// One-way latch flipped once startup migrations have been applied.
///
/// Readiness must not go green on "the database answered" alone. Between the
/// container starting and the migration finishing there is a window where the
/// store is perfectly reachable and has no tables — a service that calls
/// itself ready there is handed traffic it can only fail.
/// </summary>
public sealed class DataStoreReadiness
{
    private volatile bool _schemaReady;

    /// <summary>True once <see cref="MarkSchemaReady"/> has been called.</summary>
    public bool SchemaReady => _schemaReady;

    public void MarkSchemaReady() => _schemaReady = true;
}
