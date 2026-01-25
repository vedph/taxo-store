using Fusi.Api.Auth.Models;
using Fusi.Api.Auth.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TaxoStore.Api.Services;

/// <summary>
/// Application identity DB context.
/// </summary>
/// <seealso cref="IdentityDbContext" />
/// <remarks>Initializes a new instance of the <see cref="ApplicationDbContext"/>
/// class.</remarks>
/// <param name="options">The options.</param>
public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options) :
    IdentityDbContext<NamedUser, IdentityRole, string>(options)
{
    // https://andrewlock.net/customising-asp-net-core-identity-ef-core-naming-conventions-for-postgresql/
    // https://github.com/efcore/EFCore.NamingConventions
    // https://stackoverflow.com/questions/59286100/aspnetcore-identity-using-postgresql-how-to-create-structure

    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder)
    {
        // add the snake case naming convention
        configurationBuilder.Conventions.Add(
            _ => new SnakeCaseNamingConvention());

        base.ConfigureConventions(configurationBuilder);
    }

    /// <summary>
    /// Override this method to further configure the model that was
    /// discovered by convention from the entity types
    /// exposed in <see cref="T:Microsoft.EntityFrameworkCore.DbSet`1" />
    /// properties on your derived context. The resulting model may be cached
    /// and re-used for subsequent instances of your derived context.
    /// </summary>
    /// <param name="builder">The builder being used to construct the
    /// model for this context. Databases (and other extensions) typically
    /// define extension methods on this object that allow you to configure
    /// aspects of the model that are specific to a given database.</param>
    /// <remarks>
    /// If a model is explicitly set on the options for this context (via
    /// <see cref="M:MicrosoftDbContextOptionsBuilder.UseModel(Microsoft.EntityFrameworkCore.Metadata.IModel)" />)
    /// then this method will not be run.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // PostgreSQL uses the public schema by default - not dbo
        builder.HasDefaultSchema("public");
        base.OnModelCreating(builder);

        // rename identity tables
        builder.Entity<NamedUser>(b => b.ToTable("app_user"));

        builder.Entity<IdentityUserClaim<string>>(
            b => b.ToTable("app_user_claim"));

        builder.Entity<IdentityUserLogin<string>>(
            b => b.ToTable("app_user_login"));

        builder.Entity<IdentityUserToken<string>>(
            b => b.ToTable("app_user_token"));

        builder.Entity<IdentityRole>(b => b.ToTable("app_role"));

        builder.Entity<IdentityRoleClaim<string>>(
            b => b.ToTable("app_role_claim"));

        builder.Entity<IdentityUserRole<string>>(
            b => b.ToTable("app_user_role"));
    }
}
