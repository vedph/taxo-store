using Fusi.Api.Auth.Models;
using Fusi.Api.Auth.Services;
using Fusi.DbManager.PgSql;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Reflection;

namespace TaxoStore.Api.Services;

/// <summary>
/// Application database initializer.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ApplicationDatabaseInitializer"/>
/// class.
/// </remarks>
/// <param name="serviceProvider">The service provider.</param>
public sealed class ApplicationDatabaseInitializer(
    IServiceProvider serviceProvider) :
    AuthDatabaseInitializer<NamedUser, IdentityRole, NamedSeededUserOptions>(
        serviceProvider)
{
    /// <summary>
    /// Initializes the user.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="options">The options.</param>
    protected override void InitUser(NamedUser user,
        NamedSeededUserOptions options)
    {
        base.InitUser(user, options);

        user.FirstName = options.FirstName;
        user.LastName = options.LastName;
    }

    /// <summary>
    /// Initializes the database.
    /// </summary>
    protected override void InitDatabase()
    {
        string name = Configuration.GetValue<string>("AuthDatabaseName")!;
        Serilog.Log.Information("Checking for database {Name}...", name);

        string csTemplate = Configuration.GetConnectionString("Auth")!;
        PgSqlDbManager manager = new(csTemplate);

        if (!manager.Exists(name))
        {
            Serilog.Log.Information("Creating database {Name}...", name);

            using (StreamReader reader = new(
                Assembly.GetExecutingAssembly().GetManifestResourceStream(
                    "TaxoStore.Api.Assets.Auth.pgsql")!))
            {
                string sql = reader.ReadToEnd();
                manager.CreateDatabase(name, sql, null);
            }

            Serilog.Log.Information("Database created.");
        }
    }
}
