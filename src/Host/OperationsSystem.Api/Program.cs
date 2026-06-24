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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OperationsSystem.Api.OpenTelemetry;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

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
    .AddDbContextCheck<Audit.Infrastructure.Persistence.AuditDbContext>("audit-db", tags: ["ready"]);

// Serialize enums as their names across the API so contracts are stable and human-readable.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Each module contributes its application assembly for MediatR handlers and FluentValidation.
var moduleApplicationAssemblies = new[]
{
    Audit.Application.AssemblyReference.Assembly,
    Identity.Application.AssemblyReference.Assembly,
    MasterData.Application.AssemblyReference.Assembly
};

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblies(moduleApplicationAssemblies);
    cfg.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
});
foreach (var assembly in moduleApplicationAssemblies)
    builder.Services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

// Shared cross-cutting infrastructure.
builder.Services.AddIntegrationMessaging();
builder.Services.AddLocalFileStorage(builder.Configuration);
builder.Services.AddAuditCapture();
builder.Services.AddDurableEmail();

// Modules. Audit is registered first so its event consumer is wired before producers run.
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddMasterDataModule(builder.Configuration);

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
        // session) so credential/role/permission/suspension changes take effect immediately.
        options.Events = new JwtBearerEvents
        {
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

// Apply migrations in dependency order: Audit -> Identity -> MasterData.
await app.Services.MigrateAuditAsync();
await app.Services.MigrateAndSeedIdentityAsync();
await app.Services.MigrateAndSeedMasterDataAsync();

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;
