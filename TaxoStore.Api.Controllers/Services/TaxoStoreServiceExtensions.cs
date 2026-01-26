using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using TaxoStore.Core;
using TaxoStore.PgSql;

namespace TaxoStore.Api.Controllers.Services;

/// <summary>
/// Provides extension methods for registering TaxoStore services with an
/// IServiceCollection.
/// </summary>
/// <remarks>These extension methods simplify the integration of TaxoStore
/// repositories and related services into an application's dependency injection
/// container. Use these methods to configure and initialize TaxoStore
/// functionality when setting up your application's services.</remarks>
public static class TaxoStoreServiceExtensions
{
    /// <summary>
    /// Registers a PostgreSQL-backed implementation of the ITaxoStore service
    /// with the specified service collection.
    /// </summary>
    /// <param name="services">The service collection to which the ITaxoStore
    /// implementation will be added.</param>
    /// <param name="options">The options used to configure the PostgreSQL store,
    /// including the required connection string.</param>
    /// <exception cref="InvalidOperationException">Thrown if the ConnectionString
    /// property of options is null or empty.</exception>
    private static void RegisterPgSqlStore(
        IServiceCollection services,
        TaxoStoreServiceOptions options)
    {
        if (string.IsNullOrEmpty(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL provider requires a connection string. " +
                "Please set the ConnectionString property in " +
                "TaxoStoreServicesOptions.");
        }

        services.AddSingleton<ITaxoStore>(sp =>
        {
            TaxoStoreOptions storeOptions = options.ToTaxoStoreOptions();
            return new PgSqlTaxoStore(storeOptions);
        });
    }

    /// <summary>
    /// Adds services required for tree data storage and management to the
    /// specified service collection.
    /// </summary>
    /// <remarks>This method registers the necessary repository and, if enabled
    /// in the options, a background initialization service for tree data.
    /// It should be called during application startup to ensure all required
    /// services are available for dependency injection.</remarks>
    /// <param name="services">The service collection to which the tree services
    /// will be added.</param>
    /// <param name="configure">An optional delegate to configure the tree store
    /// service options before registration.</param>
    /// <returns>The same instance of <see cref="IServiceCollection"/> that was
    /// provided, to support method chaining.</returns>
    public static IServiceCollection AddTaxoStoreServices(
        this IServiceCollection services,
        Action<TaxoStoreServiceOptions>? configure = null)
    {
        // configure options
        TaxoStoreServiceOptions options = new();
        configure?.Invoke(options);

        // apply default connection string if not provided
        if (string.IsNullOrEmpty(options.ConnectionString))
        {
            options.ConnectionString =
                "Server=localhost;Database=taxo;User Id=postgres;" +
                "Password=postgres;Include Error Detail=True";
        }

        // apply default seed sources if not provided
        if (string.IsNullOrEmpty(options.SeedTreeSource) ||
            string.IsNullOrEmpty(options.SeedNodeSource))
        {
            // determine the seed folder directory
            string seedDir = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "taxo");

            // set default tree source if not provided but available
            if (string.IsNullOrEmpty(options.SeedTreeSource))
            {
                string treesPath = Path.Combine(seedDir, "trees.csv");
                if (File.Exists(treesPath))
                    options.SeedTreeSource = treesPath;
            }

            // set default node source if not provided but available
            if (string.IsNullOrEmpty(options.SeedNodeSource))
            {
                string nodesPath = Path.Combine(seedDir, "nodes.csv");
                if (File.Exists(nodesPath))
                    options.SeedNodeSource = nodesPath;
            }
        }

        // register the repository
        RegisterPgSqlStore(services, options);

        // register auto-initialization service if enabled
        if (options.EnableAutoInitialization)
        {
            services.AddHostedService(sp =>
            {
                ILogger<TaxoStoreInitializationService> logger =
                    sp.GetRequiredService<ILogger<TaxoStoreInitializationService>>();

                IHostApplicationLifetime lifetime =
                    sp.GetRequiredService<IHostApplicationLifetime>();

                return new TaxoStoreInitializationService(
                    sp,
                    logger,
                    lifetime,
                    options.ConnectionString!,
                    options.InitializationDelaySeconds);
            });
        }

        return services;
    }
}
