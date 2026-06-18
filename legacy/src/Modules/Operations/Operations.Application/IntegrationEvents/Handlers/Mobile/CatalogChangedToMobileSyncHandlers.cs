using BuildingBlocks.Contracts.IntegrationEvents;
using Core.Contracts.IntegrationEvents;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;

namespace Operations.Application.IntegrationEvents.Handlers.Mobile;

/// <summary>
/// Cross-module bridge between the existing catalog-change integration events
/// (raised by Core's command handlers and dispatched via the operations module's
/// outbox processor) and the mobile-sync broadcaster. Each handler emits a single
/// <c>refresh</c> envelope for the affected table — mobile's <c>SyncCoordinator</c>
/// already knows how to re-fetch the whole catalog so we don't need per-row
/// projection on this path.
/// </summary>
/// <remarks>
/// Latency note: integration events flow through the outbox processor's 10-second
/// poll, so catalog updates land on connected mobiles within ~10 s rather than
/// truly real-time. That's a reasonable trade-off for catalogs: they're admin-
/// portal-managed, change rarely, and the v2 client's defensive 5-minute polling
/// is the ultimate safety net.
/// <para>
/// Catalog <em>creates</em> and <em>deletes</em> (and Store-module catalogs —
/// tools, materials, general supports) don't have dedicated integration events
/// yet, so they're picked up by the next periodic poll on the mobile side. When
/// the source modules grow those events, register matching handlers here.
/// </para>
/// </remarks>
public sealed class ServiceNameUpdatedToMobileSyncHandler(IMobileSyncBroadcaster broadcaster)
    : IIntegrationEventHandler<ServiceNameUpdatedIntegrationEvent>
{
    public Task Handle(ServiceNameUpdatedIntegrationEvent notification, CancellationToken cancellationToken) =>
        broadcaster.BroadcastNowAsync(BuildRefresh(MobileSyncTables.Services), cancellationToken);

    private static MobileSyncChange BuildRefresh(string table) => new(
        Table: table,
        Op: MobileSyncOps.Refresh,
        EntityId: null,
        Audience: MobileSyncAudience.AllStations,
        Version: DateTimeOffset.UtcNow);
}

/// <summary>Customer IATA change → refresh the mobile customers catalog.</summary>
public sealed class CustomerIataCodeUpdatedToMobileSyncHandler(IMobileSyncBroadcaster broadcaster)
    : IIntegrationEventHandler<CustomerIataCodeUpdatedIntegrationEvent>
{
    public Task Handle(CustomerIataCodeUpdatedIntegrationEvent notification, CancellationToken cancellationToken) =>
        broadcaster.BroadcastNowAsync(
            new MobileSyncChange(
                Table: MobileSyncTables.Customers,
                Op: MobileSyncOps.Refresh,
                EntityId: null,
                Audience: MobileSyncAudience.AllStations,
                Version: DateTimeOffset.UtcNow),
            cancellationToken);
}

/// <summary>Customer deactivated → refresh the mobile customers catalog (the row drops out).</summary>
public sealed class CustomerDeactivatedToMobileSyncHandler(IMobileSyncBroadcaster broadcaster)
    : IIntegrationEventHandler<CustomerDeactivatedIntegrationEvent>
{
    public Task Handle(CustomerDeactivatedIntegrationEvent notification, CancellationToken cancellationToken) =>
        broadcaster.BroadcastNowAsync(
            new MobileSyncChange(
                Table: MobileSyncTables.Customers,
                Op: MobileSyncOps.Refresh,
                EntityId: null,
                Audience: MobileSyncAudience.AllStations,
                Version: DateTimeOffset.UtcNow),
            cancellationToken);
}

/// <summary>
/// Employee deactivated → refresh the mobile employees-at-station rosters. We can't
/// target the specific station here because <see cref="EmployeeDeactivatedIntegrationEvent"/>
/// only carries the employee id, and looking up the station from a now-inactive
/// employee record is brittle. Broadcasting to all-stations is cheap (the table is
/// small) and only the deactivated employee's station will see an actual diff.
/// </summary>
public sealed class EmployeeDeactivatedToMobileSyncHandler(IMobileSyncBroadcaster broadcaster)
    : IIntegrationEventHandler<EmployeeDeactivatedIntegrationEvent>
{
    public Task Handle(EmployeeDeactivatedIntegrationEvent notification, CancellationToken cancellationToken) =>
        broadcaster.BroadcastNowAsync(
            new MobileSyncChange(
                Table: MobileSyncTables.Employees,
                Op: MobileSyncOps.Refresh,
                EntityId: null,
                Audience: MobileSyncAudience.AllStations,
                Version: DateTimeOffset.UtcNow),
            cancellationToken);
}
