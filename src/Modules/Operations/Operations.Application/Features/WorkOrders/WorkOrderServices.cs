using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Domain.Enumerations;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.WorkOrders;

public interface IWorkOrderTimelineWriter
{
    public Task AppendAsync(
        Guid workOrderId,
        WorkOrderTimelineEventType eventType,
        DateTimeOffset occurredAtUtc,
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
        Guid workOrderId,
        WorkOrderTimelineEventType eventType,
        DateTimeOffset occurredAtUtc,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        var actorName = await ResolveActorNameAsync(cancellationToken);
        db.WorkOrderTimelineEntries.Add(new WorkOrderTimelineEntry(
            workOrderId, eventType, occurredAtUtc, user.UserId ?? Guid.Empty, actorName, details));
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

public interface IWorkOrderNumberAllocator
{
    public Task<Result<(int Sequence, string Number)>> AllocateAsync(StationSnapshot station, CancellationToken cancellationToken);
}

public sealed class WorkOrderNumberAllocator(IOperationsDbContext db) : IWorkOrderNumberAllocator
{
    public async Task<Result<(int Sequence, string Number)>> AllocateAsync(StationSnapshot station, CancellationToken cancellationToken)
    {
        var used = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Status == WorkOrderStatus.Approved && w.Station.StationId == station.StationId && w.ApprovalSequence != null)
            .Select(w => w.ApprovalSequence!.Value)
            .OrderBy(sequence => sequence)
            .ToListAsync(cancellationToken);

        var next = 1;
        foreach (var sequence in used)
        {
            if (sequence > next)
                break;
            if (sequence == next)
                next++;
        }

        var number = WorkOrderNumber.Format(station.IataCode, next);
        return number.IsFailure ? number.Error : (next, number.Value);
    }
}
