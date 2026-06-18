using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.Employee;
using Core.Contracts.Readers;

namespace Operations.Application.Features.Flight;

internal static class FlightEmployeeSnapshotResolver
{
    internal static async Task<Result<IReadOnlyDictionary<Guid, EmployeeSnapshot>>> ResolveAsync(
        IEmployeeReader employeeReader,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var distinct = employeeIds.Distinct().ToList();
        var map = new Dictionary<Guid, EmployeeSnapshot>(distinct.Count);
        foreach (var id in distinct)
        {
            var snapshot = await employeeReader.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return Result<IReadOnlyDictionary<Guid, EmployeeSnapshot>>.Failure(
                    Error.Validation($"Employee '{id}' was not found or has incomplete station/manpower profile data."));
            }

            map[id] = snapshot;
        }

        return Result<IReadOnlyDictionary<Guid, EmployeeSnapshot>>.Success(map);
    }

    internal static List<EmployeeSnapshot> MapAssignments(
        IReadOnlyDictionary<Guid, EmployeeSnapshot> resolved,
        IReadOnlyList<EmployeeSnapshot> requestedOrder) =>
        requestedOrder.Select(e => resolved[e.EmployeeId]).ToList();
}
