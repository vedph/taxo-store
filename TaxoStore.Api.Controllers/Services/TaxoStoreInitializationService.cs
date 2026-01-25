using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using TaxoStore.Core;

namespace TaxoStore.Api.Controllers.Services;

/// <summary>
/// Provides a background service that initializes the TaxoStore database when
/// the application starts.
/// </summary>
/// <remarks>This service performs database initialization asynchronously during
/// application startup. If initialization fails critically, the application
/// will be stopped. An optional delay can be configured before initialization
/// begins. This service is typically registered as a hosted service in the
/// application's dependency injection container.</remarks>
public sealed class TaxoStoreInitializationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TaxoStoreInitializationService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    //private readonly string _connectionStringOrPath;
    private readonly int _delaySeconds;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaxoStoreInitializationService"/>
    /// class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="lifetime">The application lifetime.</param>
    /// <param name="connectionStringOrPath">The connection string or
    /// file path.</param>
    /// <param name="delaySeconds">The delay in seconds before initialization
    /// starts.</param>
    public TaxoStoreInitializationService(
        IServiceProvider serviceProvider,
        ILogger<TaxoStoreInitializationService> logger,
        IHostApplicationLifetime lifetime,
        string connectionStringOrPath,
        int delaySeconds = 0)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _lifetime = lifetime;
        //_connectionStringOrPath = connectionStringOrPath;
        _delaySeconds = delaySeconds;
    }

    /// <summary>
    /// Executes the background database initialization asynchronously.
    /// </summary>
    /// <param name="stoppingToken">The cancellation token.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // apply initialization delay if configured
            if (_delaySeconds > 0)
            {
                _logger.LogInformation(
                    "Waiting {DelaySeconds} seconds before database initialization...",
                    _delaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(_delaySeconds), stoppingToken);
            }

            // use a scope to resolve the repository
            using IServiceScope scope = _serviceProvider.CreateScope();
            ITaxoStore store = scope.ServiceProvider
                .GetRequiredService<ITaxoStore>();

            // initialize the database
            _logger.LogInformation("Initializing TaxoStore database...");

            DateTime startTime = DateTime.UtcNow;
            await store.InitializeAsync();
            TimeSpan duration = DateTime.UtcNow - startTime;

            _logger.LogInformation("TaxoStore database initialization " +
                "completed in {Duration:c}.", duration);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("TaxoStore database initialization was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initializing " +
                "the TaxoStore database. The application will shut down.");

            // stop the application if database initialization fails critically
            _lifetime.StopApplication();
        }
    }
}
