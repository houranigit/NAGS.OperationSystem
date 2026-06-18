using System.Linq.Dynamic.Core;
using Audit.Application;
using BuildingBlocks.Application.DependencyInjection;
using BuildingBlocks.Infrastructure.DependencyInjection;
using Contracts.Application;
using Contracts.Infrastructure;
using Core.Application;
using Core.Infrastructure;
using Core.Infrastructure.Seeding;
using Host.Web.Components;
using Host.Web.Configuration;
using Host.Web.Extensions;
using Host.Web.Services;
using Identity.Application;
using Identity.Infrastructure;
using Identity.Infrastructure.Seeding;
using Identity.Presentation;
using MediatR;
using Notifications.Application;
using Notifications.Infrastructure;
using Notifications.Presentation;
using Operations.Application;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using Operations.Infrastructure;
using Operations.Presentation;
using Operations.Presentation.Mobile.Sync;
using Scalar.AspNetCore;
using Store.Application;
using Store.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Teach System.Linq.Dynamic.Core about every domain enum so Radzen DataGrid filter
// expressions (e.g. "Status == Operations.Domain.Enumerations.FlightStatus.Scheduled")
// are resolved when handlers run query.Where(filterString) on IQueryable<TEntity>.
ParsingConfig.Default.CustomTypeProvider = new DynamicLinqEnumTypeProvider(
    typeof(Operations.Domain.Enumerations.FlightStatus).Assembly,
    typeof(Core.Domain.Enumerations.Manufacturer).Assembly,
    typeof(Identity.Domain.Enumerations.UserStatus).Assembly);

// The mobile-sync broadcast behavior MUST be registered before AddBuildingBlocksApplication
// so MediatR's first-registered-is-outermost ordering puts it OUTSIDE TransactionBehavior.
// That ordering is what guarantees IMobileSyncBroadcaster.FlushAsync runs after the
// unit-of-work commits — otherwise we could push a change the server rolled back.
builder.Services.AddTransient(
    typeof(IPipelineBehavior<,>),
    typeof(MobileSyncBroadcastBehavior<,>));

builder.Services.AddBuildingBlocksApplication(
    typeof(IdentityApplicationMarker).Assembly,
    typeof(AuditApplicationMarker).Assembly,
    typeof(CoreApplicationMarker).Assembly,
    typeof(ContractsApplicationMarker).Assembly,
    typeof(OperationsApplicationMarker).Assembly,
    typeof(NotificationsApplicationMarker).Assembly,
    typeof(StoreApplicationMarker).Assembly);

builder.Services.AddBuildingBlocksInfrastructure(builder.Configuration);

builder.Services.AddIdentityModuleCore(builder.Configuration);
builder.Services.AddCoreModule(builder.Configuration);
builder.Services.AddStoreModule(builder.Configuration);
builder.Services.AddContractsModule(builder.Configuration);
builder.Services.AddOperationsModule(builder.Configuration);
builder.Services.AddOperationsMobilePresentation();
builder.Services.AddNotificationsModule(builder.Configuration);
builder.Services.AddNotificationsPresentation();
builder.Services.AddOperationsWebAuthentication(builder.Configuration);
builder.Services.AddWebServices();
builder.Services.Configure<PlatformSettings>(builder.Configuration.GetSection(PlatformSettings.SectionName));
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader();
        policy.AllowAnyMethod();
        policy.AllowAnyOrigin();
    });
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await app.Services.ApplyMigrationsAsync();

await RoleSeeder.SeedAsync(app.Services);
await AdminSeeder.SeedAsync(app.Services, builder.Configuration);
await CoreDataSeeder.SeedAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Do not re-execute Razor /not-found for API or SignalR — that replaces JSON 401/403/404
// responses with an HTML shell and breaks mobile clients.
app.UseWhen(
    static ctx => !(ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || ctx.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase)),
    static branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapAuthEndpoints();

app.MapStaticAssets();

app.MapIdentityEndpoints();
app.MapOperationsMobileEndpoints();
app.MapNotificationsEndpoints();
// Real-time mobile-sync hub: every v2 Android client connects here after login
// to receive incremental change envelopes. Sits alongside the notifications hub.
app.MapHub<MobileSyncHub>(MobileSyncHub.Path);

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program { }
