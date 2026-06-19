using System.Text;
using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Application.Behaviors;
using FluentValidation;
using Identity.Api;
using Identity.Infrastructure;
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

// Application messaging: MediatR handlers + the Result-first validation behavior + validators.
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(Identity.Application.AssemblyReference.Assembly);
    cfg.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(Identity.Application.AssemblyReference.Assembly, includeInternalTypes: true);

// Modules.
builder.Services.AddIdentityModule(builder.Configuration);

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

await app.Services.MigrateAndSeedIdentityAsync();

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;
