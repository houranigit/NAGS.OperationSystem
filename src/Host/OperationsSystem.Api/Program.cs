using System.Text;
using System.Text.Json.Serialization;
using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Security;
using BuildingBlocks.Application.Behaviors;
using BuildingBlocks.Infrastructure.Messaging;
using BuildingBlocks.Infrastructure.Storage;
using FluentValidation;
using Identity.Api;
using Identity.Infrastructure;
using MasterData.Api;
using MasterData.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
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
builder.Services.AddHealthChecks();

// Serialize enums as their names across the API so contracts are stable and human-readable.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Each module contributes its application assembly for MediatR handlers and FluentValidation.
var moduleApplicationAssemblies = new[]
{
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

// Modules.
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
    });
builder.Services.AddPermissionAuthorization();
builder.Services.AddHttpUserContext();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

new IdentityEndpointModule().MapEndpoints(app);
new MasterDataEndpointModule().MapEndpoints(app);

await app.Services.MigrateAndSeedIdentityAsync();
await app.Services.MigrateAndSeedMasterDataAsync();

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;
