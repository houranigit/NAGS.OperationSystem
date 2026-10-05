using OperationsSystem.Blazor.Client.Features.Operations.Components;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Features.Operations;

/// <summary>Maps the dashboard's local calendar selections to its inclusive/exclusive UTC API range.</summary>
internal sealed record DashboardLocalDateRange(
    DateTime? FromDate,
    DateTime? ToDate,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc)
{
    public static DateTime Today(UserTimeZone timeZone, DateTimeOffset nowUtc) =>
        timeZone.ToLocal(nowUtc).Date;

    public static DateTimeOffset NextMidnightUtc(UserTimeZone timeZone, DateTimeOffset nowUtc) =>
        timeZone.DateBoundaryUtc(Today(timeZone, nowUtc), endOfDay: true).AddTicks(1);

    public static DashboardLocalDateRange ForDates(UserTimeZone timeZone, DateTime fromDate, DateTime toDate) =>
        new(fromDate.Date, toDate.Date,
            timeZone.DateBoundaryUtc(fromDate, endOfDay: false),
            timeZone.DateBoundaryUtc(toDate, endOfDay: true).AddTicks(1));

    public static DashboardLocalDateRange? ForPreset(
        UserTimeZone timeZone, DashboardPeriodPreset preset, DateTimeOffset nowUtc)
    {
        var today = Today(timeZone, nowUtc);
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        return preset switch
        {
            DashboardPeriodPreset.Today => ForDates(timeZone, today, today),
            DashboardPeriodPreset.LastMonth => ForDates(timeZone, thisMonth.AddMonths(-1), thisMonth.AddDays(-1)),
            DashboardPeriodPreset.LastThreeMonths => ForDates(timeZone, today.AddMonths(-3), today),
            DashboardPeriodPreset.Max => new(null, null, null, null),
            _ => null
        };
    }
}
