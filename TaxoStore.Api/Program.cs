using Fusi.Api.Auth.Models;
using Fusi.Api.Auth.Services;
using MessagingApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaxoStore.Api.Controllers;
using TaxoStore.Api.Controllers.Services;
using TaxoStore.Api.Services;

namespace TaxoStore.Api;

public static partial class Program
{
    #region CORS
    private static void ConfigureCorsServices(IServiceCollection services,
        ConfigurationManager config)
    {
        string[] origins = ["http://localhost:4200"];

        IConfigurationSection section = config.GetSection("AllowedOrigins");
        if (section.Exists())
        {
            origins = section.AsEnumerable()
                .Where(p => !string.IsNullOrEmpty(p.Value))
                .Select(p => p.Value).ToArray()!;
        }

        services.AddCors(o => o.AddPolicy("CorsPolicy", builder =>
        {
            builder.AllowAnyMethod()
                .AllowAnyHeader()
                // https://github.com/aspnet/SignalR/issues/2110 for AllowCredentials
                .AllowCredentials()
                .WithOrigins(origins);
        }));
    }
    #endregion

    #region Messaging
    private static void ConfigureMessagingServices(IServiceCollection services,
        ConfigurationManager configuration)
    {
        // configuration sections
        // https://andrewlock.net/adding-validation-to-strongly-typed-configuration-objects-in-asp-net-core/
        services.Configure<MessagingOptions>(configuration.GetSection("Messaging"));
        services.Configure<DotNetMailerOptions>(configuration.GetSection("Mailer"));

        // explicitly register the settings object by delegating to the IOptions object
        services.AddSingleton(resolver =>
            resolver.GetRequiredService<IOptions<MessagingOptions>>().Value);
        services.AddSingleton(resolver =>
            resolver.GetRequiredService<IOptions<DotNetMailerOptions>>().Value);

        // services
        services.AddScoped<IMailerService, DotNetMailerService>();
        services.AddScoped<IMessageBuilderService,
            FileMessageBuilderService>();
    }
    #endregion

    #region Auth
    private static void ConfigureAuthServices(IServiceCollection services,
        ConfigurationManager configuration)
    {
        // identity
        string csTemplate = configuration.GetConnectionString("Auth")!;

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(string.Format(csTemplate,
                configuration.GetValue<string>("AuthDatabaseName")));
        });

        services.AddIdentity<NamedUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        // authentication service
        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
        .AddJwtBearer(options =>
        {
            // NOTE: remember to set the values in configuration:
            // Jwt:SecureKey, Jwt:Audience, Jwt:Issuer
            IConfigurationSection jwtSection = configuration.GetSection("Jwt");
            string? key = jwtSection["SecureKey"];
            if (string.IsNullOrEmpty(key))
                throw new InvalidOperationException("Required JWT SecureKey not found");

            options.SaveToken = true;
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters = new TokenValidationParameters()
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidAudience = jwtSection["Audience"],
                ValidIssuer = jwtSection["Issuer"],
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(key))
            };

            // support refresh
            // https://stackoverflow.com/questions/55150099/jwt-token-expiration-time-failing-net-core
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    if (context.Exception.GetType() ==
                            typeof(SecurityTokenExpiredException))
                    {
                        context.Response.Headers.Append("Token-Expired", "true");
                    }
                    return Task.CompletedTask;
                }
            };
        });
#if DEBUG
        // use to show more information when troubleshooting JWT issues
        IdentityModelEventSource.ShowPII = true;
#endif
    }
    #endregion

    #region Taxo Store
    private static void ConfigureTaxoStoreServices(IServiceCollection services,
        ConfigurationManager config)
    {
        services.AddTreeServices(options =>
        {
            // get connection string
            string? connectionString = config.GetConnectionString("TaxoStore");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'TaxoStore' not found.");
            }
            options.ConnectionString = connectionString;

            // read auto-initialization flag
            options.EnableAutoInitialization =
                config.GetValue("TaxoStore:EnableAutoInitialization", true);

            // read initialization delay (useful for Docker/PostgreSQL startup)
            options.InitializationDelaySeconds =
                config.GetValue("TaxoStore:InitializationDelaySeconds", 0);
        });
    }
    #endregion

    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // get configuration
        IConfiguration config = new ConfigurationService(builder.Environment)
            .Configuration;

        // add services to the container
        builder.Services.AddControllers();
        builder.Services.AddOpenApi();

        // add CORS services
        ConfigureCorsServices(builder.Services, builder.Configuration);

        // add controllers from other assemblies
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(TaxoTreeController).Assembly)
            .AddControllersAsServices();

        // user repository service
        builder.Services.AddScoped<IUserRepository<NamedUser>,
            UserRepository<NamedUser, IdentityRole>>();

        // configure auth services
        ConfigureMessagingServices(builder.Services, builder.Configuration);
        ConfigureAuthServices(builder.Services, builder.Configuration);

        // configure tree store services
        ConfigureTaxoStoreServices(builder.Services, builder.Configuration);

        // application
        WebApplication app = builder.Build();

        // seed auth database
        await app.SeedAuthAsync();

        // configure the HTTP request pipeline
        app.MapOpenApi();
        app.MapScalarApiReference(options => options.WithTitle("Tree Store Test API"));

        // https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-nginx?view=aspnetcore-2.2#configure-a-reverse-proxy-server
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
        });

        // CORS
        app.UseCors("CorsPolicy");

        // configure the HTTP request pipeline
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            // https://docs.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-5.0&tabs=visual-studio
            app.UseExceptionHandler("/Error");
            if (config.GetValue<bool>("Server:UseHSTS"))
            {
                Console.WriteLine("Using HSTS");
                app.UseHsts();
            }
        }

        app.UseAuthorization();
        app.MapControllers();
        app.Run();
    }
}
