using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Audit.Api;
using Audit.Infrastructure;
using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.RateLimiting;
using BuildingBlocks.Api.Security;
using BuildingBlocks.Application.Behaviors;
using BuildingBlocks.Infrastructure.Auditing;
using BuildingBlocks.Infrastructure.Email;
using BuildingBlocks.Infrastructure.Messaging;
using BuildingBlocks.Infrastructure.Storage;
using FluentValidation;
using Identity.Api;
using Identity.Application.Abstractions;
using Identity.Infrastructure;
using Identity.Infrastructure.Security;
using MasterData.Api;
using MasterData.Infrastructure;
using Notifications.Api;
using Notifications.Api.Realtime;
using Notifications.Infrastructure;
using Operations.Api;
using Operations.Api.Mobile;
using Operations.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OperationsSystem.Api.OpenTelemetry;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
AddLocalConfigurationFile(builder);

var useSerilog = builder.Configuration.GetValue<bool?>("Logging:UseSerilog") ?? true;
if (useSerilog)
{
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());
}

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddOpenApi();

builder.Services.AddOperationsOpenTelemetry(builder.Configuration);

// Readiness includes the module databases so orchestrators only route traffic once data stores are
// reachable; liveness ("/health/live") stays a cheap process check.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<Identity.Infrastructure.Persistence.IdentityDbContext>("identity-db", tags: ["ready"])
    .AddDbContextCheck<MasterData.Infrastructure.Persistence.MasterDataDbContext>("masterdata-db", tags: ["ready"])
    .AddDbContextCheck<Notifications.Infrastructure.Persistence.NotificationsDbContext>("notifications-db", tags: ["ready"])
    .AddDbContextCheck<Operations.Infrastructure.Persistence.OperationsDbContext>("operations-db", tags: ["ready"])
    .AddDbContextCheck<Audit.Infrastructure.Persistence.AuditDbContext>("audit-db", tags: ["ready"]);

// Serialize enums as their names across the API so contracts are stable and human-readable.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Each module contributes its application assembly for MediatR handlers and FluentValidation.
var moduleApplicationAssemblies = new[]
{
    Audit.Application.AssemblyReference.Assembly,
    Identity.Application.AssemblyReference.Assembly,
    MasterData.Application.AssemblyReference.Assembly,
    Notifications.Application.AssemblyReference.Assembly,
    Operations.Application.AssemblyReference.Assembly
};

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblies(moduleApplicationAssemblies);
    // MobileSyncBroadcastBehavior is registered first (outermost) so the mobile-sync buffer
    // flushes only after validation and the handler (including its SaveChanges) succeeded.
    cfg.AddOpenBehavior(typeof(BuildingBlocks.Application.Mobile.MobileSyncBroadcastBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
});
foreach (var assembly in moduleApplicationAssemblies)
    builder.Services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

// Shared cross-cutting infrastructure.
var dispatchOutbox = builder.Configuration.GetValue<bool?>("Messaging:OutboxDispatchEnabled") ?? true;
builder.Services.AddIntegrationMessaging(dispatchOutbox: dispatchOutbox);
builder.Services.AddLocalFileStorage(builder.Configuration);
builder.Services.AddAuditCapture();
builder.Services.AddDurableEmail(builder.Configuration, builder.Environment);

// Modules. Audit is registered first so its event consumer is wired before producers run.
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddMasterDataModule(builder.Configuration);
builder.Services.AddNotificationsModule(builder.Configuration);
builder.Services.AddNotificationsApi();
builder.Services.AddOperationsModule(builder.Configuration);

// Mobile offline-sync: SignalR hub + per-request change broadcaster.
builder.Services.AddMobileSync();

// Compose the cross-module permission catalog after all module catalogs are registered.
builder.Services.AddPermissionRegistry();

// Authentication (JWT Bearer) + permission-based authorization.
var jwt = builder.Configuration.GetSection("Identity:Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SigningKey"] ?? string.Empty)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // Validate the token against live state (active user, current security stamp, active
        // session). The validator may cache briefly to avoid a remote DB trip on every API call.
        options.Events = new JwtBearerEvents
        {
            // SignalR WebSocket upgrades cannot carry an Authorization header, so the mobile
            // client passes the bearer token as ?access_token= on hub paths.
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                if (principal is null)
                {
                    context.Fail("No principal.");
                    return;
                }

                if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
                {
                    context.Fail("Missing subject.");
                    return;
                }

                var stamp = principal.FindFirstValue(IdentityClaimTypes.SecurityStamp);
                Guid? sessionId = Guid.TryParse(principal.FindFirstValue(IdentityClaimTypes.SessionId), out var sid)
                    ? sid
                    : null;

                var validator = context.HttpContext.RequestServices.GetRequiredService<ITokenSecurityValidator>();
                if (!await validator.IsCurrentAsync(userId, stamp, sessionId, context.HttpContext.RequestAborted))
                    context.Fail("Token is no longer valid.");
            }
        };
    });
builder.Services.AddPermissionAuthorization();
builder.Services.AddHttpUserContext();
builder.Services.AddHttpAuditContext();

// Rate limit abuse-prone anonymous auth endpoints, partitioned by client IP. Limited callers get a
// 429 instead of a chance to brute-force credentials or enumerate accounts.
var authPermitLimit = builder.Configuration.GetValue("Security:RateLimit:AnonymousAuthPermitLimit", 10);
var authWindowSeconds = builder.Configuration.GetValue("Security:RateLimit:AnonymousAuthWindowSeconds", 60);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.AnonymousAuth, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromSeconds(authWindowSeconds),
                QueueLimit = 0
            }));
});

var app = builder.Build();

WarnForSlowSqlConnectionSettings(app.Logger, app.Configuration);

// Assign/propagate a correlation id per request and enrich logs with it.
app.Use(async (context, next) =>
{
    const string header = "X-Correlation-ID";
    var correlationId = context.Request.Headers.TryGetValue(header, out var existing) && !string.IsNullOrWhiteSpace(existing)
        ? existing.ToString()
        : context.TraceIdentifier;

    context.Response.Headers[header] = correlationId;
    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        await next();
});

if (useSerilog)
    app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // liveness: process is up; no dependency checks
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

new AuditEndpointModule().MapEndpoints(app);
new IdentityEndpointModule().MapEndpoints(app);
new MasterDataEndpointModule().MapEndpoints(app);
new NotificationsEndpointModule().MapEndpoints(app);
new OperationsEndpointModule().MapEndpoints(app);

app.MapHub<Operations.Api.Mobile.MobileSyncHub>(Operations.Api.Mobile.MobileSyncHub.Path);
app.MapHub<NotificationsHub>(NotificationsHub.Path);

var applyMigrationsOnStartup = app.Configuration.GetValue<bool?>("Database:ApplyMigrationsOnStartup")
    ?? app.Environment.IsDevelopment();

if (applyMigrationsOnStartup)
{
    // Apply migrations in dependency order: Audit -> Identity -> MasterData.
    await app.Services.MigrateAuditAsync();
    await app.Services.MigrateAndSeedIdentityAsync();
    await app.Services.MigrateAndSeedMasterDataAsync();
    await app.Services.MigrateOperationsAsync();
    await app.Services.MigrateNotificationsAsync();
}
else
{
    app.Logger.LogInformation("Skipping startup database migrations and seed data. Apply migrations before serving traffic.");
}

app.Run();

static void WarnForSlowSqlConnectionSettings(Microsoft.Extensions.Logging.ILogger logger, IConfiguration configuration)
{
    string[] connectionNames = ["Default", "Identity", "MasterData", "Operations", "Notifications", "Audit"];
    var inspected = new HashSet<string>(StringComparer.Ordinal);

    foreach (var connectionName in connectionNames)
    {
        var connectionString = configuration.GetConnectionString(connectionName);
        if (string.IsNullOrWhiteSpace(connectionString) || !inspected.Add(connectionString))
            continue;

        try
        {
            var parsed = new DbConnectionStringBuilder { ConnectionString = connectionString };

            if (TryGetBool(parsed, "Pooling") is false)
            {
                logger.LogWarning(
                    "Connection string '{ConnectionStringName}' has Pooling=False. Remote SQL Server requests will open a new physical connection for each EF command.",
                    connectionName);
            }

            if (TryGetInt(parsed, "Command Timeout") == 0 || TryGetInt(parsed, "Default Command Timeout") == 0)
            {
                logger.LogWarning(
                    "Connection string '{ConnectionStringName}' has an infinite SQL command timeout. Use a bounded timeout so slow SQL fails visibly.",
                    connectionName);
            }
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Could not inspect connection string '{ConnectionStringName}' for performance settings.", connectionName);
        }
    }
}

static bool? TryGetBool(DbConnectionStringBuilder builder, string key)
{
    if (!builder.TryGetValue(key, out var value))
        return null;

    if (value is bool boolean)
        return boolean;

    return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed)
        ? parsed
        : null;
}

static int? TryGetInt(DbConnectionStringBuilder builder, string key)
{
    if (!builder.TryGetValue(key, out var value))
        return null;

    if (value is int number)
        return number;

    return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : null;
}

static void AddLocalConfigurationFile(WebApplicationBuilder builder)
{
    builder.Configuration.AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.local.json",
        optional: true,
        reloadOnChange: true);

    var sources = builder.Configuration.Sources;
    var localSource = sources[^1];
    sources.RemoveAt(sources.Count - 1);

    var insertIndex = sources.Count;
    for (var i = 0; i < sources.Count; i++)
    {
        if (IsExternalOverrideSource(sources[i]))
        {
            insertIndex = i;
            break;
        }
    }

    sources.Insert(insertIndex, localSource);
}

static bool IsExternalOverrideSource(Microsoft.Extensions.Configuration.IConfigurationSource source)
{
    var typeName = source.GetType().FullName;
    return typeName is
        "Microsoft.Extensions.Configuration.UserSecrets.UserSecretsConfigurationSource" or
        "Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationSource" or
        "Microsoft.Extensions.Configuration.CommandLine.CommandLineConfigurationSource";
}

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;
