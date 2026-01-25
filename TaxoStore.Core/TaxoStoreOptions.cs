namespace TaxoStore.Core;

/// <summary>
/// Options for <see cref="TaxoStore"/> implementors.
/// </summary>
public class TaxoStoreOptions
{
    /// <summary>
    /// The source of the tree store (e.g., database connection string).
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// True if <see cref="SeedTreeSource"/> and <see cref="SeedNodeSource"/>
    /// are a CSV text to be read for seeding, rather than a path pointing to
    /// a CSV file for seeding.
    /// </summary>
    public bool SeedSourceAsText { get; set; }

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
}
