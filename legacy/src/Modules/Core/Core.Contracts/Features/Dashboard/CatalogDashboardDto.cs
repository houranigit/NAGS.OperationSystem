namespace Core.Contracts.Features.Dashboard;

/// <summary>
/// Snapshot of catalog and roster data for the home dashboard. All counts are
/// "current state" — they do not depend on a time window.
/// </summary>
public sealed record CatalogDashboardDto(
    int TotalCustomers,
    int ActiveCustomers,
    int TotalEmployees,
    int ActiveEmployees,
    int TotalStations,
    int TotalCountries,
    int TotalServices,
    int ActiveServices,
    int TotalManpowerTypes,
    int ActiveManpowerTypes,
    int TotalLicenses,
    int TotalAircraftTypes,
    IReadOnlyList<EmployeesByStationRow> EmployeesByStation,
    IReadOnlyList<EmployeesByManpowerRow> EmployeesByManpower,
    IReadOnlyList<LicenseDistributionRow> LicenseDistribution
);

public sealed record EmployeesByStationRow(Guid StationId, string IataCode, string Name, int Count);

public sealed record EmployeesByManpowerRow(Guid ManpowerTypeId, string Name, int Count);

/// <summary>
/// Per-license rollup with how many employees currently hold it. Only active
/// employees count toward <see cref="EmployeeCount"/>; inactive holders are
/// excluded so the dashboard tracks live coverage.
/// </summary>
public sealed record LicenseDistributionRow(Guid LicenseId, string Code, string Name, int EmployeeCount);
