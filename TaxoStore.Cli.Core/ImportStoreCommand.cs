using Fusi.Cli.Config;
using Fusi.Cli.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Threading;
using System.Threading.Tasks;
using TaxoStore.Core;
using TaxoStore.PgSql;

namespace TaxoStore.Cli.Core;

/// <summary>
/// Creates and seeds a TaxoStore PostgreSQL database from CSV sources, using
/// the same initialization logic used by the TaxoStore API
/// (<see cref="PgSqlTaxoStore.InitializeAsync"/>): the database is created
/// only when it does not already exist, and immediately seeded from the
/// specified CSV files right after creation. If the database already
/// exists, it is left untouched.
/// </summary>
public sealed class ImportStoreCommand : AsyncCommand<ImportStoreSettings>
{
    /// <summary>
    /// Executes the import.
    /// </summary>
    /// <param name="context">The command context.</param>
    /// <param name="settings">The command settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<int> ExecuteAsync(CommandContext context,
        ImportStoreSettings settings, CancellationToken cancellationToken)
    {
        // resolve the connection string template, either from the command
        // line or from the tool's configuration (appsettings.json /
        // appsettings.local.json / environment variables)
        string? template = settings.ConnectionTemplate;
        if (string.IsNullOrEmpty(template))
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddCliDefaults()
                .Build();
            template = config.GetConnectionString("Default");
        }

        if (string.IsNullOrEmpty(template))
        {
            AnsiConsole.MarkupLine("[red]No connection string template " +
                "found. Specify one with -c/--connection, or configure a " +
                "'Default' connection string in appsettings.json.[/]");
            return 1;
        }

        string connectionString = string.Format(template, settings.Database);

        TaxoStoreOptions options = new()
        {
            Source = connectionString,
            SeedSourceAsText = false,
            SeedTreeSource = settings.TreesPath,
            SeedNodeSource = settings.NodesPath
        };

        ILogger<PgSqlTaxoStore> logger = LogService.CreateLogger<PgSqlTaxoStore>();

        AnsiConsole.MarkupLine("Importing TaxoStore database [cyan]{0}[/]:",
            settings.Database.EscapeMarkup());
        AnsiConsole.MarkupLine("  trees:  [grey]{0}[/]",
            settings.TreesPath.EscapeMarkup());
        AnsiConsole.MarkupLine("  nodes:  [grey]{0}[/]",
            settings.NodesPath.EscapeMarkup());

        try
        {
            using PgSqlTaxoStore store = new(options, logger);

            await store.InitializeAsync();

            AnsiConsole.MarkupLine("[green]Import completed.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Import failed: {0}[/]",
                ex.Message.EscapeMarkup());
            return 2;
        }
    }
}
