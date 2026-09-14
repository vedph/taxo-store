using Spectre.Console.Cli;
using System.ComponentModel;
using System.IO;
using ValidationResult = Spectre.Console.ValidationResult;

namespace TaxoStore.Cli.Core;

/// <summary>
/// Settings for <see cref="ImportStoreCommand"/>.
/// </summary>
public sealed class ImportStoreSettings : CommandSettings
{
    /// <summary>
    /// Path to the CSV file with tree definitions.
    /// </summary>
    [CommandArgument(0, "<TREES_CSV>")]
    [Description("Path to the CSV file with tree definitions.")]
    public string TreesPath { get; set; } = "";

    /// <summary>
    /// Path to the CSV file with node definitions.
    /// </summary>
    [CommandArgument(1, "<NODES_CSV>")]
    [Description("Path to the CSV file with node definitions.")]
    public string NodesPath { get; set; } = "";

    /// <summary>
    /// The name of the target database to create and seed.
    /// </summary>
    [CommandOption("-d|--database <NAME>")]
    [Description("The name of the target database to create and seed.")]
    public string Database { get; set; } = "";

    /// <summary>
    /// The PostgreSQL connection string template, with <c>{0}</c> as a
    /// placeholder for the database name. When not specified, it is read
    /// from the "Default" connection string in the tool's configuration
    /// (appsettings.json/appsettings.local.json/environment variables).
    /// </summary>
    [CommandOption("-c|--connection <TEMPLATE>")]
    [Description("The PostgreSQL connection string template, with {0} as a " +
        "placeholder for the database name. When not specified, it is read " +
        "from the 'Default' connection string in the tool's configuration.")]
    public string? ConnectionTemplate { get; set; }

    /// <summary>
    /// Validates the settings.
    /// </summary>
    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            return ValidationResult.Error(
                "A target database name is required (-d/--database).");
        }

        if (!File.Exists(TreesPath))
        {
            return ValidationResult.Error(
                $"Trees CSV file not found: {TreesPath}");
        }

        if (!File.Exists(NodesPath))
        {
            return ValidationResult.Error(
                $"Nodes CSV file not found: {NodesPath}");
        }

        return ValidationResult.Success();
    }
}
