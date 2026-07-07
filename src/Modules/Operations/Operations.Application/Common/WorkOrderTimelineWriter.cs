using BuildingBlocks.Application.Abstractions;
using MasterData.Contracts.Readers;
using Operations.Application.Abstractions;
using Operations.Domain.Enumerations;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Common;

public interface IWorkOrderTimelineWriter
{
    public Task AppendAsync(
        WorkOrder workOrder,
        WorkOrderTimelineEventType eventType,
        DateTimeOffset occurredAtUtc,
        string? workOrderNumber = null,
        string? details = null,
        CancellationToken cancellationToken = default);
}

public sealed class WorkOrderTimelineWriter(
    IOperationsDbContext db,
    IUserContext user,
    IMasterDataReader masterData) : IWorkOrderTimelineWriter
{
    private string? _resolvedActorName;
    private bool _actorNameResolved;

    public async Task AppendAsync(
        WorkOrder workOrder,
        WorkOrderTimelineEventType eventType,
        DateTimeOffset occurredAtUtc,
        string? workOrderNumber = null,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        var actorName = await ResolveActorNameAsync(cancellationToken);
        db.WorkOrderTimelineEntries.Add(new WorkOrderTimelineEntry(
            workOrder.Id,
            workOrder.FlightId,
            eventType,
            occurredAtUtc,
            user.UserId ?? Guid.Empty,
            actorName,
            workOrderNumber,
            details));
    }

    private async Task<string?> ResolveActorNameAsync(CancellationToken cancellationToken)
    {
        if (_actorNameResolved)
            return _resolvedActorName;

        _actorNameResolved = true;
        if (user.ExternalReferenceId is { } staffId)
        {
            var staff = await masterData.GetStaffMemberAsync(staffId, cancellationToken);
            _resolvedActorName = staff?.FullName;
        }

        return _resolvedActorName;
    }
}
