using TaxoStore.Core;

namespace TaxoStore.Api.Controllers.Services;

/// <summary>
/// Options for configuring the TaxoStore service.
/// </summary>
public class TaxoStoreServiceOptions
{
    /// <summary>
    /// Gets or sets the PostgreSQL connection string.
    /// For example: "Server=localhost;Database=trees;User Id=postgres;Password=pwd".
    /// If null, defaults to a local PostgreSQL instance with default credentials.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets whether to enable automatic database initialization.
    /// When true, the database will be automatically created and seeded if 
    /// it doesn't exist.
    /// Default is true.
    /// </summary>
    public bool EnableAutoInitialization { get; set; } = true;

    /// <summary>
    /// Gets or sets the delay in seconds before database initialization starts.
    /// This is useful in Docker environments where the database service needs
    /// time to start.
    /// For example, set to 5-10 seconds to allow PostgreSQL to fully initialize.
    /// Default is 0 (no delay).
    /// </summary>
    public int InitializationDelaySeconds { get; set; } = 0;

    /// <summary>
    /// Gets or sets either a CSV file or the file system path to the CSV
    /// seeded trees file used for initialization.
    /// </summary>
    public string? SeedTreeSource { get; set; }

    /// <summary>
    /// Gets or sets either a CSV file or the file system path to the CSV
    /// seeded nodes file used for initialization.
    /// </summary>
    public string? SeedNodeSource { get; set; }

    /// <summary>
    /// Creates a new instance of the TaxoStoreOptions class using the current
    /// configuration values.
    /// </summary>
    /// <returns>A TaxoStoreOptions object initialized with the current
    /// connection string, seed tree source, and seed nodesource.</returns>
    public TaxoStoreOptions ToTaxoStoreOptions()
    {
        return new TaxoStoreOptions
        {
            Source = ConnectionString ?? "",
            SeedTreeSource = SeedTreeSource,
            SeedNodeSource = SeedNodeSource
        };
    }
}
