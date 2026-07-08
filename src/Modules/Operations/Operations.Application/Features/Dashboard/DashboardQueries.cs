using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Application.Features.Flights;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.Dashboard;

public sealed record GetOperationsDashboardQuery : IQuery<OperationsDashboardDto>;

public sealed class GetOperationsDashboardQueryHandler(IOperationsDbContext db, IOperationsScope scope)
    : IQueryHandler<GetOperationsDashboardQuery, OperationsDashboardDto>
{
    public async Task<Result<OperationsDashboardDto>> Handle(GetOperationsDashboardQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var flights = db.Flights.AsNoTracking().AsQueryable();
        if (!scopeResult.Value.IsAdministrator && scopeResult.Value.StationId is { } stationId)
            flights = flights.Where(f => f.Station.StationId == stationId);

        var scheduled = await flights.CountAsync(f => f.Status == FlightStatus.Scheduled, cancellationToken);
        var inProgress = await flights.CountAsync(f => f.Status == FlightStatus.InProgress, cancellationToken);
        var completed = await flights.CountAsync(f => f.Status == FlightStatus.Completed, cancellationToken);
        var canceled = await flights.CountAsync(f => f.Status == FlightStatus.Canceled, cancellationToken);

        return new OperationsDashboardDto(scheduled, inProgress, completed, canceled);
    }
}

// --- Duplicate candidates lookup (called by the ad-hoc UI before creating) ---

public sealed record FindDuplicateCandidatesQuery(
    Guid CustomerId,
    Guid? StationId,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? ExcludeFlightId = null) : IQuery<IReadOnlyList<DuplicateCandidateDto>>;

public sealed class FindDuplicateCandidatesQueryHandler(IOperationsScope scope, FlightDuplicateDetector detector)
    : IQueryHandler<FindDuplicateCandidatesQuery, IReadOnlyList<DuplicateCandidateDto>>
{
    public async Task<Result<IReadOnlyList<DuplicateCandidateDto>>> Handle(FindDuplicateCandidatesQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var context = scopeResult.Value;
        Guid stationId;
        if (context.IsAdministrator)
        {
            if (request.StationId is not { } requestedStationId || requestedStationId == Guid.Empty)
                return Error.Validation("Station is required to check for duplicates.", "Operations.Flight.DuplicateCheckStationRequired");

            stationId = requestedStationId;
        }
        else if (context.StationId is { } scopedStationId)
        {
            if (request.StationId is { } requestedStationId && requestedStationId != Guid.Empty && requestedStationId != scopedStationId)
                return Error.Forbidden("This duplicate check is outside your station scope.", "Operations.Scope.Forbidden");

            stationId = scopedStationId;
        }
        else
        {
            return Error.Forbidden("You do not have access to duplicate checks.", "Operations.Flight.DuplicateCheckNotAllowed");
        }

        var candidates = await detector.FindAsync(
            request.CustomerId,
            stationId,
            request.ScheduledArrivalUtc,
            request.ScheduledDepartureUtc,
            request.ExcludeFlightId,
            cancellationToken);
        return Result.Success(candidates);
    }
}
