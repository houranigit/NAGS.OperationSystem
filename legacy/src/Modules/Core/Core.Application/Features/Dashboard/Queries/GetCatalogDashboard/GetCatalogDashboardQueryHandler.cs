using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.Dashboard.Queries.GetCatalogDashboard;

/// <summary>
/// Loads a point-in-time snapshot of master data (customers, employees, services,
/// manpower, licenses, etc.) for the home dashboard. Counts are not scoped to a
/// time window.
/// </summary>
/// <remarks>
/// Performance notes:
/// <list type="bullet">
///   <item>Each <i>total + active</i> pair is collapsed into a single round-trip via
///         <c>GroupBy(_ => 1)</c>, which the SQL provider translates to one
///         <c>SELECT COUNT(*), SUM(CASE WHEN IsActive THEN 1 ELSE 0 END)</c> per table.</item>
///   <item>The three rollup queries (<c>employeesByStation</c>, <c>employeesByManpower</c>,
///         <c>licenseDistribution</c>) each use a two-step pattern: GROUP BY the scalar FK
///         + Take(N) on the server, then a single <c>WHERE Id IN (…)</c> against the lookup
///         table, then in-memory join. The naive <c>GroupBy(...).Join(...).Select(... .Value ...)</c>
///         shape does <b>not</b> translate in EF Core 10 — the optimizer can't compose a
///         strongly-typed-ID <c>.Value</c> access (HasConversion) <i>or</i> an <c>OwnsOne</c>
///         <c>.Value</c> access (e.g. <c>Station.IataCode.Value</c>) inside the join's
///         result selector. Materializing both sides first sidesteps the entire problem;
///         each side is bounded to N rows so the second round-trip is trivial.</item>
///   <item>All read queries are unchanged from a tracking perspective: projections to
///         non-entity DTOs are inherently no-tracking.</item>
/// </list>
/// </remarks>
public sealed class GetCatalogDashboardQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetCatalogDashboardQuery, CatalogDashboardDto>
{
    public async Task<Result<CatalogDashboardDto>> Handle(
        GetCatalogDashboardQuery request,
        CancellationToken cancellationToken)
    {
        // Total + active in a single SQL statement per table. The helper takes a
        // pre-projected bool stream so the predicate stays inside an IQueryable and
        // there's no Expression<>-vs-Func<> hand-off inside the GroupBy.
        var customerCounts = await CountTotalAndActiveAsync(db.Customers.Select(c => c.IsActive), cancellationToken);
        var employeeCounts = await CountTotalAndActiveAsync(db.Employees.Select(e => e.IsActive), cancellationToken);
        var serviceCounts = await CountTotalAndActiveAsync(db.Services.Select(s => s.IsActive), cancellationToken);
        var manpowerCounts = await CountTotalAndActiveAsync(db.ManpowerTypes.Select(m => m.IsActive), cancellationToken);

        // Plain scalar counts for catalogs that don't carry an IsActive flag.
        var totalStations = await db.Stations.CountAsync(cancellationToken);
        var totalCountries = await db.Countries.CountAsync(cancellationToken);
        var totalLicenses = await db.Licenses.CountAsync(cancellationToken);
        var totalAircraftTypes = await db.AircraftTypes.CountAsync(cancellationToken);

        // ── employeesByStation: top 8 by active headcount ─────────────────────────
        // GROUP BY + Take in SQL, then materialize the matching Stations and join in memory.
        // (See <remarks> on why a single-trip Join.Select doesn't translate.)
        var stationBuckets = await db.Employees
            .Where(e => e.IsActive)
            .GroupBy(e => e.StationId)
            .Select(g => new { StationId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(8)
            .ToListAsync(cancellationToken);

        var stationIds = stationBuckets.Select(x => x.StationId).ToList();

        var stationEntities = stationIds.Count == 0
            ? []
            : await db.Stations
                .Where(s => stationIds.Contains(s.Id))
                .ToListAsync(cancellationToken);

        var employeesByStation = stationBuckets
            .Join(
                stationEntities,
                b => b.StationId,
                s => s.Id,
                (b, s) => new EmployeesByStationRow(s.Id.Value, s.IataCode.Value, s.Name, b.Count))
            .OrderByDescending(x => x.Count)
            .ToList();

        // ── employeesByManpower: top 8 by active headcount ────────────────────────
        var manpowerBuckets = await db.Employees
            .Where(e => e.IsActive)
            .GroupBy(e => e.ManpowerTypeId)
            .Select(g => new { ManpowerTypeId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(8)
            .ToListAsync(cancellationToken);

        var manpowerIds = manpowerBuckets.Select(x => x.ManpowerTypeId).ToList();

        var manpowerEntities = manpowerIds.Count == 0
            ? []
            : await db.ManpowerTypes
                .Where(m => manpowerIds.Contains(m.Id))
                .ToListAsync(cancellationToken);

        var employeesByManpower = manpowerBuckets
            .Join(
                manpowerEntities,
                b => b.ManpowerTypeId,
                m => m.Id,
                (b, m) => new EmployeesByManpowerRow(m.Id.Value, m.Name, b.Count))
            .OrderByDescending(x => x.Count)
            .ToList();

        // ── licenseDistribution: top 10 licenses currently held by active employees ──
        var licenseBuckets = await db.EmployeeLicenses
            .Where(el => db.Employees.Any(e => e.Id == el.EmployeeId && e.IsActive))
            .GroupBy(el => el.LicenseId)
            .Select(g => new { LicenseId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(cancellationToken);

        var licenseIds = licenseBuckets.Select(x => x.LicenseId).ToList();

        var licenseEntities = licenseIds.Count == 0
            ? []
            : await db.Licenses
                .Where(l => licenseIds.Contains(l.Id))
                .ToListAsync(cancellationToken);

        var licenseDistribution = licenseBuckets
            .Join(
                licenseEntities,
                b => b.LicenseId,
                l => l.Id,
                (b, l) => new LicenseDistributionRow(l.Id.Value, l.Code, l.Name, b.Count))
            .OrderByDescending(x => x.EmployeeCount)
            .ToList();

        var dto = new CatalogDashboardDto(
            customerCounts.Total,
            customerCounts.Active,
            employeeCounts.Total,
            employeeCounts.Active,
            totalStations,
            totalCountries,
            serviceCounts.Total,
            serviceCounts.Active,
            manpowerCounts.Total,
            manpowerCounts.Active,
            totalLicenses,
            totalAircraftTypes,
            employeesByStation,
            employeesByManpower,
            licenseDistribution);

        return Result<CatalogDashboardDto>.Success(dto);
    }

    /// <summary>
    /// Returns total and "active" counts for the given bool stream in a single SQL
    /// round-trip by aggregating with <c>GroupBy(_ =&gt; 1)</c>. The provider translates
    /// the projection to <c>COUNT(*)</c> + <c>SUM(CASE WHEN ... THEN 1 ELSE 0 END)</c>.
    /// </summary>
    private static async Task<(int Total, int Active)> CountTotalAndActiveAsync(
        IQueryable<bool> activeFlags,
        CancellationToken cancellationToken)
    {
        var stats = await activeFlags
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Active = g.Sum(flag => flag ? 1 : 0)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return stats is null ? (0, 0) : (stats.Total, stats.Active);
    }
}
