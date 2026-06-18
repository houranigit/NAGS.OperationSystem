using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Contracts.Dashboard;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.Dashboard.Queries.GetOperationsDashboard;

/// <summary>
/// Aggregates flight + work-order counts for the home dashboard. Each slice is a single
/// GroupBy / Count over the trailing window, executed sequentially against
/// <see cref="IOperationsDbContext"/>; nothing is materialized until the per-slice
/// projection. Cheap enough to run on every dashboard load.
/// </summary>
/// <remarks>
/// EF Core does <b>not</b> translate <c>GroupBy</c> over <c>OwnsOne</c> snapshot fields
/// (<c>Station</c>, <c>Customer</c>) — even when an intermediate flat <c>Select</c> is
/// inserted, the query optimizer folds the projection back and re-introduces owned-type
/// property access in the GroupBy key, which the SQL generator cannot translate.
/// The robust workaround is to project the snapshot's primitive columns into memory
/// (a thin "id + iata + name" tuple per flight in the window) and group client-side.
/// The data volume is bounded by the lookback window — a few thousand rows at most for
/// 90 days — so the in-memory grouping cost is trivial and the SQL is a plain projection.
/// </remarks>
public sealed class GetOperationsDashboardQueryHandler(IOperationsDbContext db)
    : IQueryHandler<GetOperationsDashboardQuery, OperationsDashboardDto>
{
    public async Task<Result<OperationsDashboardDto>> Handle(
        GetOperationsDashboardQuery request,
        CancellationToken cancellationToken)
    {
        var lookBack = request.LookBackDays <= 0 ? 30 : request.LookBackDays;
        var nowUtc = DateTimeOffset.UtcNow;
        var fromUtc = nowUtc.AddDays(-lookBack);

        // "Today" uses the server clock — good enough for a dashboard tile.
        var todayStart = new DateTimeOffset(nowUtc.Date, TimeSpan.Zero);
        var tomorrowStart = todayStart.AddDays(1);

        // Status breakdown for the window — primitive enum key translates fine.
        var statusGroups = await db.Flights
            .Where(f => f.Schedule.Sta >= fromUtc)
            .GroupBy(f => f.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Status(FlightStatus s) => statusGroups.FirstOrDefault(x => x.Key == s)?.Count ?? 0;

        var scheduled = Status(FlightStatus.Scheduled);
        var inProgress = Status(FlightStatus.InProgress);
        var completed = Status(FlightStatus.Completed);
        var canceled = Status(FlightStatus.Canceled);
        var totalFlights = scheduled + inProgress + completed + canceled;

        var settled = completed + canceled;
        var completionRate = settled == 0
            ? 0m
            : Math.Round((decimal)completed * 100m / settled, 1);

        var flightsToday = await db.Flights
            .CountAsync(f => f.Schedule.Sta >= todayStart && f.Schedule.Sta < tomorrowStart, cancellationToken);

        // Top 8 stations by flight volume in the window.
        // Project the snapshot's primitive columns to memory and GroupBy client-side
        // (see <remarks> on the class — owned-type GroupBy can't be translated).
        var stationRows = await db.Flights
            .Where(f => f.Schedule.Sta >= fromUtc)
            .Select(f => new
            {
                StationId = f.Station.StationId,
                IataCode = f.Station.IataCode,
                Name = f.Station.Name
            })
            .ToListAsync(cancellationToken);

        var topStations = stationRows
            .GroupBy(x => new { x.StationId, x.IataCode, x.Name })
            .Select(g => new FlightsByStationRow(
                g.Key.StationId,
                g.Key.IataCode,
                g.Key.Name,
                g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(8)
            .ToList();

        // Top 5 customers by flight volume in the window — same client-side grouping.
        var customerRows = await db.Flights
            .Where(f => f.Schedule.Sta >= fromUtc)
            .Select(f => new
            {
                CustomerId = f.Customer.CustomerId,
                IataCode = f.Customer.IataCode,
                Name = f.Customer.Name
            })
            .ToListAsync(cancellationToken);

        var topCustomers = customerRows
            .GroupBy(x => new { x.CustomerId, x.IataCode, x.Name })
            .Select(g => new FlightsByCustomerRow(
                g.Key.CustomerId,
                g.Key.IataCode,
                g.Key.Name,
                g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        // Last 14-day trend — pull just the timestamps and bucket client-side.
        // 14 days of flight timestamps is cheap (a few thousand rows max), and avoids
        // any EF translation gymnastics around DateTimeOffset.Date over an owned type.
        var dayWindowStart = todayStart.AddDays(-13);
        var rawDays = await db.Flights
            .Where(f => f.Schedule.Sta >= dayWindowStart && f.Schedule.Sta < tomorrowStart)
            .Select(f => f.Schedule.Sta)
            .ToListAsync(cancellationToken);

        var byDay = Enumerable.Range(0, 14)
            .Select(offset =>
            {
                var date = DateOnly.FromDateTime(dayWindowStart.UtcDateTime.AddDays(offset));
                var count = rawDays.Count(s => DateOnly.FromDateTime(s.UtcDateTime) == date);
                return new FlightsByDayRow(date, count);
            })
            .ToList();

        // Work-order status breakdown (created in the window) — primitive enum, fine.
        var woGroups = await db.WorkOrders
            .Where(w => w.CreatedAt >= fromUtc)
            .GroupBy(w => w.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Wo(WorkOrderStatus s) => woGroups.FirstOrDefault(x => x.Key == s)?.Count ?? 0;

        var dto = new OperationsDashboardDto(
            lookBack,
            totalFlights,
            flightsToday,
            scheduled,
            inProgress,
            completed,
            canceled,
            completionRate,
            Wo(WorkOrderStatus.UnderReview),
            Wo(WorkOrderStatus.Approved),
            Wo(WorkOrderStatus.Rejected),
            Wo(WorkOrderStatus.Deleting),
            topStations,
            topCustomers,
            byDay);

        return Result<OperationsDashboardDto>.Success(dto);
    }
}
