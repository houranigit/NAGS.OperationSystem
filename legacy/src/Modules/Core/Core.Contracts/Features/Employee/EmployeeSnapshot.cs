using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.Station;

namespace Core.Contracts.Features.Employee;

public sealed record EmployeeSnapshot
{
    public Guid EmployeeId { get; init; }
    public string FullName { get; init; } = null!;
    public StationSnapshot StationSnapshot { get; init; } = null!;
    public ManpowerTypeSnapshot ManpowerTypeSnapshot { get; init; } = null!;

    public EmployeeSnapshot() { }

    public EmployeeSnapshot(
        Guid employeeId,
        string fullName,
        StationSnapshot stationSnapshot,
        ManpowerTypeSnapshot manpowerTypeSnapshot)
    {
        EmployeeId = employeeId;
        FullName = fullName;
        StationSnapshot = stationSnapshot;
        ManpowerTypeSnapshot = manpowerTypeSnapshot;
    }
}
