using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.Employee;
using Core.Contracts.Readers;

namespace Core.Application.Features.Employee.Queries.GetEmployeeSnapshotsAtStation;

/// <summary>
/// Thin wrapper around <see cref="IEmployeeReader.SearchActiveSnapshotsByStationAsync"/>
/// so the portal Blazor dialogs can consume station-scoped employee snapshots through the
/// same MediatR pipeline they use for everything else (logging, validation, transactions).
/// </summary>
public sealed class GetEmployeeSnapshotsAtStationQueryHandler(IEmployeeReader employeeReader)
    : IQueryHandler<GetEmployeeSnapshotsAtStationQuery, IReadOnlyList<EmployeeSnapshot>>
{
    public async Task<Result<IReadOnlyList<EmployeeSnapshot>>> Handle(
        GetEmployeeSnapshotsAtStationQuery request,
        CancellationToken cancellationToken)
    {
        if (request.StationId == Guid.Empty)
            return Error.Validation("Station id is required.");

        var take = Math.Clamp(request.Take, 1, 1000);
        var rows = await employeeReader.SearchActiveSnapshotsByStationAsync(
            request.StationId,
            request.Search,
            take,
            cancellationToken);

        return Result<IReadOnlyList<EmployeeSnapshot>>.Success(rows);
    }
}
