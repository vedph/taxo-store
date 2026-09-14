using Fusi.Cli.Logging;
using Serilog;
using Serilog.Events;
using Spectre.Console.Cli;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TaxoStore.Cli.Core;

namespace TaxoStore.Cli;

/// <summary>
/// Main program.
/// </summary>
public static class Program
{
#if DEBUG
    private static void DeleteLogs()
    {
        foreach (var path in Directory.EnumerateFiles(
            AppDomain.CurrentDomain.BaseDirectory, "taxo-log*.txt"))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }
    }
#endif

    /// <summary>
    /// Sets the console cursor visibility, swallowing the <see
    /// cref="IOException"/> that Windows throws when the process has no
    /// real console handle attached (e.g. redirected/piped output, or
    /// running under some CI/automation hosts).
    /// </summary>
    private static void TrySetCursorVisible(bool visible)
    {
        try
        {
            Console.CursorVisible = visible;
        }
        catch (IOException) { }
        catch (PlatformNotSupportedException) { }
    }

    /// <summary>
    /// Entry point.
    /// </summary>
    /// <param name="args">The arguments.</param>
    public static async Task<int> Main(string[] args)
    {
        LogService? logService = null;

        try
        {
#if DEBUG
            DeleteLogs();
#endif
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (IOException) { }

            // create and configure the LogService instance from the reusable library.
            LogServiceOptions options = new()
            {
                FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "taxo-log-.txt"),
                OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss} " +
                    "[{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}",
                FileRollingInterval = RollingInterval.Day,
                RetainedFileCountLimit = 7,
                UseSerilogConsoleSink = true,
#if DEBUG
                MinimumLevel = LogEventLevel.Debug
#else
                MinimumLevel = LogEventLevel.Information
#endif
            };

            logService = new LogService(options);

            Stopwatch stopwatch = new();
            stopwatch.Start();

            CommandApp app = new();
            app.Configure(config =>
            {
                config.AddCommand<ImportStoreCommand>("import-store")
                      .WithDescription("Import store data from the specified CSV sources.");
            });

            int result = await app.RunAsync(args);

            Console.ResetColor();
            TrySetCursorVisible(true);
            Console.WriteLine();
            Console.WriteLine();

            stopwatch.Stop();
            Console.WriteLine("\nTime: {0}h{1}'{2}\"",
                    stopwatch.Elapsed.Hours,
                    stopwatch.Elapsed.Minutes,
                    stopwatch.Elapsed.Seconds);

            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
            TrySetCursorVisible(true);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.ToString());
            Console.ResetColor();
            return 2;
        }
        finally
        {
            // dispose the LogService (flushes Serilog and disables SelfLog if enabled)
            try
            {
                logService?.Dispose();
            }
            catch
            {
                // swallow to avoid masking exceptions on shutdown
            }
        }
    }
}
