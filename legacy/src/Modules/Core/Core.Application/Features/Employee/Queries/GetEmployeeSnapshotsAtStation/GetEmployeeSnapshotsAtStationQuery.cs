using BuildingBlocks.Application.Abstractions.Queries;
using Core.Contracts.Features.Employee;

namespace Core.Application.Features.Employee.Queries.GetEmployeeSnapshotsAtStation;

/// <summary>
/// Returns active employees at a given station as <see cref="EmployeeSnapshot"/> rows
/// (full name + station + manpower-type names). Used by the portal Work Order dialog
/// to populate Service-line and Task employee pickers with the entire station roster
/// — not just the flight's assigned crew — so a work order can record work performed
/// by anyone at the station.
/// </summary>
/// <param name="StationId">The station to scope the roster to.</param>
/// <param name="Search">Optional contains-match on full name.</param>
/// <param name="Take">Cap the result size; defaults to 500 to comfortably cover the largest stations.</param>
public sealed record GetEmployeeSnapshotsAtStationQuery(
    Guid StationId,
    string? Search = null,
    int Take = 500) : IQuery<IReadOnlyList<EmployeeSnapshot>>;
