using BuildingBlocks.Application.Abstractions;
using MasterData.Contracts.Readers;
using Operations.Application.Abstractions;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;

namespace Operations.Application.Common;

/// <summary>
/// Appends entries to a flight's portal-visible timeline. Entries are added to the current unit of
/// work; the calling handler's SaveChanges persists them in the same transaction as the state change.
/// </summary>
public interface IFlightTimelineWriter
{
    public Task AppendAsync(
        Guid flightId,
        FlightTimelineEventType eventType,
        DateTimeOffset occurredAtUtc,
        string? details = null,
        CancellationToken cancellationToken = default);
}

public sealed class FlightTimelineWriter(
    IOperationsDbContext db,
    IUserContext user,
    IMasterDataReader masterData) : IFlightTimelineWriter
{
    private string? _resolvedActorName;
    private bool _actorNameResolved;

    public async Task AppendAsync(
        Guid flightId,
        FlightTimelineEventType eventType,
        DateTimeOffset occurredAtUtc,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        var actorName = await ResolveActorNameAsync(cancellationToken);
        db.FlightTimelineEntries.Add(new FlightTimelineEntry(
            flightId, eventType, occurredAtUtc, user.UserId ?? Guid.Empty, actorName, details));
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
