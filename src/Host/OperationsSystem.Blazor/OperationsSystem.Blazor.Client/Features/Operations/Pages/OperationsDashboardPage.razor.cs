using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Localization;
using OperationsSystem.Blazor.Client.Shared;
using OperationsSystem.Blazor.Client.State;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.Operations.Pages;

public partial class OperationsDashboardPage : IAsyncDisposable
{
    private const string GridKey = "operations-dashboard-flights";
    private static readonly TimeSpan LiveRefreshInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FilterAutoApplyDelay = TimeSpan.FromMilliseconds(250);
    private static readonly int[] PageSizes = [10, 25, 50, 100];
    private static DateTime UtcToday => DateTime.UtcNow.Date;

    private readonly CancellationTokenSource lifetimeCts = new();
    private CancellationTokenSource? dashboardRequestCts;
    private CancellationTokenSource? tableRequestCts;
    private CancellationTokenSource? filterAutoApplyCts;
    private Task? pollingTask;
    private DataListCard<DashboardFlightRow>? flightList;

    private OperationsDashboard? dashboard;
    private IReadOnlyList<DashboardFlightRow> flightRows = [];
    private IReadOnlyList<DashboardFilterOption> stationOptions = [];
    private IReadOnlyList<DashboardFilterOption> customerOptions = [];
    private IReadOnlyList<DashboardFilterOption> serviceOptions = [];

    private DashboardRangeMode selectedRangeMode = DashboardRangeMode.Live;
    private DashboardRangeMode activeRangeMode = DashboardRangeMode.Live;
    private DateTime? selectedDay;
    private DateTime? selectedFromDate;
    private DateTime? selectedToDate;
    private DateTime? calendarSelection;
    private bool isRangeStartPending;
    private IEnumerable<Guid>? selectedStationIds = [];
    private IEnumerable<Guid>? selectedCustomerIds = [];
    private IEnumerable<Guid>? selectedServiceIds = [];
    private DashboardFilter appliedFilter = default!;

    private long flightTotalCount;
    private int currentPage = 1;
    private int currentPageSize = 10;
    private string? currentSort;
    private bool isInitialLoading = true;
    private bool isRefreshing;
    private bool isTableLoading;
    private bool isExporting;
    private bool dashboardLoadError;
    private bool tableLoadError;

    [Inject] private AuthSession Auth { get; set; } = default!;
    [Inject] private OperationsApiClient Operations { get; set; } = default!;
    [Inject] private NotificationService Notifications { get; set; } = default!;
    [Inject] private GridPreferences GridPrefs { get; set; } = default!;

    private int FlightTotalCount => flightTotalCount > int.MaxValue ? int.MaxValue : (int)flightTotalCount;
    private bool IsLiveEnabled => selectedRangeMode == DashboardRangeMode.Live;

    private string RangeSummary => activeRangeMode switch
    {
        DashboardRangeMode.Live => string.Format(
            UiStrings.OperationsDashboard.LiveTodayFormat,
            UtcToday.ToString("dd MMM yyyy", CultureInfo.CurrentCulture)),
        DashboardRangeMode.SingleDay => appliedFilter.FromDate.ToString("dddd, dd MMM yyyy", CultureInfo.CurrentCulture),
        _ => string.Format(
            UiStrings.OperationsDashboard.PeriodFormat,
            appliedFilter.FromDate.ToString("dd MMM yyyy", CultureInfo.CurrentCulture),
            appliedFilter.ToDate.ToString("dd MMM yyyy", CultureInfo.CurrentCulture))
    };

    private string LastUpdatedLabel => dashboard is null
        ? UiStrings.OperationsDashboard.WaitingForData
        : string.Format(
            UiStrings.OperationsDashboard.UpdatedAtFormat,
            dashboard.GeneratedAtUtc.UtcDateTime.ToString("HH:mm:ss 'UTC'", CultureInfo.CurrentCulture));

    private string CalendarSelectionSummary => IsLiveEnabled
        ? UtcToday.ToString("dd MMM yyyy", CultureInfo.CurrentCulture)
        : selectedRangeMode == DashboardRangeMode.Period && selectedFromDate is { } from
            ? selectedToDate is { } to
                ? string.Format(
                    UiStrings.OperationsDashboard.PeriodFormat,
                    from.ToString("dd MMM yyyy", CultureInfo.CurrentCulture),
                    to.ToString("dd MMM yyyy", CultureInfo.CurrentCulture))
                : from.ToString("dd MMM yyyy", CultureInfo.CurrentCulture)
            : (selectedDay ?? UtcToday).ToString("dd MMM yyyy", CultureInfo.CurrentCulture);

    private string CalendarHint => IsLiveEnabled
        ? UiStrings.OperationsDashboard.LiveModeDescription
        : selectedRangeMode == DashboardRangeMode.Period
            ? isRangeStartPending
                ? UiStrings.OperationsDashboard.SelectRangeEnd
                : selectedToDate is null
                    ? UiStrings.OperationsDashboard.SelectRangeStart
                    : UiStrings.OperationsDashboard.RangeSelected
            : UiStrings.OperationsDashboard.SelectDay;

    private IReadOnlyList<DashboardTrendPoint> LocalizedMonthlyPoints =>
        dashboard?.Monthly
            .Select(point => point is { SortOrder: >= 1 and <= 12 }
                ? point with
                {
                    Label = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(point.SortOrder)
                }
                : point)
            .ToList() ?? [];

    private IReadOnlyList<StatusSegment> StatusSegments
    {
        get
        {
            if (dashboard is null)
                return [];

            var offset = 0d;
            var segments = new List<StatusSegment>(dashboard.Statuses.Count);
            foreach (var status in dashboard.Statuses.Where(item => item.FlightCount > 0))
            {
                segments.Add(new StatusSegment(
                    status.Status,
                    StatusLabel(status.Status),
                    StatusTone(status.Status),
                    status.FlightCount,
                    status.Percentage,
                    offset));
                offset += status.Percentage;
            }

            return segments;
        }
    }

    protected override void OnInitialized()
    {
        var today = UtcToday;
        selectedDay = today;
        selectedFromDate = today;
        selectedToDate = today;
        calendarSelection = today;
        appliedFilter = BuildFilter(DashboardRangeMode.Live, today, today);

        Auth.StateChanged += OnAuthStateChanged;
        TryStartPolling();
    }

    private void TryStartPolling()
    {
        if (pollingTask is not null ||
            Auth.Status != AuthStatus.Authenticated ||
            !Auth.HasPermission(OperationsPermissions.DashboardAnalyticsView))
        {
            return;
        }

        pollingTask = InitializeAndPollAsync(lifetimeCts.Token);
    }

    private async Task InitializeAndPollAsync(CancellationToken cancellationToken)
    {
        try
        {
            currentPageSize = await GridPrefs.GetPageSizeAsync(GridKey, currentPageSize, PageSizes);
            if (await LoadDashboardAsync(cancellationToken))
                await ReloadFlightsAsync();

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(LiveRefreshInterval, cancellationToken);
                if (activeRangeMode != DashboardRangeMode.Live)
                    continue;

                var liveFilter = BuildFilter(
                    DashboardRangeMode.Live,
                    UtcToday,
                    UtcToday);
                liveFilter = liveFilter with
                {
                    StationIds = appliedFilter.StationIds,
                    CustomerIds = appliedFilter.CustomerIds,
                    ServiceIds = appliedFilter.ServiceIds
                };
                if (await LoadDashboardAsync(
                        cancellationToken,
                        includeOptions: false,
                        requestedFilter: liveFilter,
                        requestedRangeMode: DashboardRangeMode.Live))
                {
                    await ReloadFlightsAsync();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the page is left or the session ends.
        }
        catch (Exception ex)
        {
            await InvokeAsync(() => DispatchExceptionAsync(ex));
        }
    }

    private Task OnStationSelectionChangedAsync(IEnumerable<Guid>? values)
    {
        selectedStationIds = values?.Distinct().ToList() ?? [];
        return ScheduleDimensionFilterApplyAsync();
    }

    private Task OnCustomerSelectionChangedAsync(IEnumerable<Guid>? values)
    {
        selectedCustomerIds = values?.Distinct().ToList() ?? [];
        return ScheduleDimensionFilterApplyAsync();
    }

    private Task OnServiceSelectionChangedAsync(IEnumerable<Guid>? values)
    {
        selectedServiceIds = values?.Distinct().ToList() ?? [];
        return ScheduleDimensionFilterApplyAsync();
    }

    private async Task ScheduleDimensionFilterApplyAsync()
    {
        filterAutoApplyCts?.Cancel();
        filterAutoApplyCts?.Dispose();
        filterAutoApplyCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
        var requestToken = filterAutoApplyCts.Token;

        try
        {
            await Task.Delay(FilterAutoApplyDelay, requestToken);
            var nextFilter = appliedFilter with
            {
                StationIds = SelectedIds(selectedStationIds),
                CustomerIds = SelectedIds(selectedCustomerIds),
                ServiceIds = SelectedIds(selectedServiceIds)
            };
            await ApplyDashboardFilterAsync(nextFilter, activeRangeMode, requestToken);
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            // A newer automatic filter selection superseded this one.
        }
    }

    private async Task ToggleLiveAsync()
    {
        if (isRefreshing)
            return;

        var previousMode = selectedRangeMode;
        var previousDay = selectedDay;
        var previousFrom = selectedFromDate;
        var previousTo = selectedToDate;
        var previousCalendarSelection = calendarSelection;
        var previousRangeState = isRangeStartPending;
        var today = UtcToday;

        selectedRangeMode = IsLiveEnabled
            ? DashboardRangeMode.SingleDay
            : DashboardRangeMode.Live;
        selectedDay = today;
        selectedFromDate = today;
        selectedToDate = today;
        calendarSelection = today;
        isRangeStartPending = false;

        var nextFilter = BuildFilter(selectedRangeMode, today, today);
        if (await ApplyDashboardFilterAsync(nextFilter, selectedRangeMode, lifetimeCts.Token))
            return;

        selectedRangeMode = previousMode;
        selectedDay = previousDay;
        selectedFromDate = previousFrom;
        selectedToDate = previousTo;
        calendarSelection = previousCalendarSelection;
        isRangeStartPending = previousRangeState;
    }

    private async Task SelectCalendarModeAsync(DashboardRangeMode mode)
    {
        if (IsLiveEnabled || mode == selectedRangeMode)
            return;

        if (mode == DashboardRangeMode.Period)
        {
            selectedRangeMode = DashboardRangeMode.Period;
            selectedFromDate = null;
            selectedToDate = null;
            isRangeStartPending = false;
            return;
        }

        var previousMode = selectedRangeMode;
        var day = selectedDay ?? selectedToDate ?? selectedFromDate ?? UtcToday;
        selectedRangeMode = DashboardRangeMode.SingleDay;
        selectedDay = day;
        selectedFromDate = day;
        selectedToDate = day;
        calendarSelection = day;
        isRangeStartPending = false;

        var nextFilter = BuildFilter(DashboardRangeMode.SingleDay, day, day);
        if (!await ApplyDashboardFilterAsync(
                nextFilter,
                DashboardRangeMode.SingleDay,
                lifetimeCts.Token))
        {
            selectedRangeMode = previousMode;
        }
    }

    private async Task OnCalendarDateChangedAsync(DateTime? value)
    {
        if (IsLiveEnabled || value is null)
            return;

        var selectedDate = value.Value.Date > UtcToday ? UtcToday : value.Value.Date;
        calendarSelection = selectedDate;

        if (selectedRangeMode == DashboardRangeMode.SingleDay)
        {
            selectedDay = selectedDate;
            selectedFromDate = selectedDate;
            selectedToDate = selectedDate;
            var dayFilter = BuildFilter(DashboardRangeMode.SingleDay, selectedDate, selectedDate);
            await ApplyDashboardFilterAsync(
                dayFilter,
                DashboardRangeMode.SingleDay,
                lifetimeCts.Token);
            return;
        }

        if (!isRangeStartPending)
        {
            selectedFromDate = selectedDate;
            selectedToDate = null;
            isRangeStartPending = true;
            return;
        }

        var firstDate = selectedFromDate?.Date ?? selectedDate;
        selectedFromDate = firstDate <= selectedDate ? firstDate : selectedDate;
        selectedToDate = firstDate <= selectedDate ? selectedDate : firstDate;
        selectedDay = selectedToDate;
        calendarSelection = selectedToDate;
        isRangeStartPending = false;

        var rangeFilter = BuildFilter(
            DashboardRangeMode.Period,
            selectedFromDate.Value,
            selectedToDate.Value);
        await ApplyDashboardFilterAsync(
            rangeFilter,
            DashboardRangeMode.Period,
            lifetimeCts.Token);
    }

    private void RenderCalendarDate(DateRenderEventArgs args)
    {
        if (args.Date.Date > UtcToday)
        {
            args.Disabled = true;
            return;
        }

        var date = args.Date.Date;
        string? markerClass = null;
        if (selectedRangeMode == DashboardRangeMode.Period && selectedFromDate is { } from)
        {
            if (date == from.Date || date == selectedToDate?.Date)
                markerClass = "od-calendar-range-edge";
            else if (selectedToDate is { } to && date > from.Date && date < to.Date)
                markerClass = "od-calendar-range-day";
        }
        else if (date == selectedDay?.Date)
        {
            markerClass = "od-calendar-selected-day";
        }

        if (markerClass is null)
            return;

        var existingClass = args.Attributes.TryGetValue("class", out var value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;
        args.Attributes["class"] = string.IsNullOrWhiteSpace(existingClass)
            ? markerClass
            : $"{existingClass} {markerClass}";
    }

    private async Task<bool> ApplyDashboardFilterAsync(
        DashboardFilter filter,
        DashboardRangeMode rangeMode,
        CancellationToken cancellationToken)
    {
        if (!await LoadDashboardAsync(
                cancellationToken,
                includeOptions: false,
                requestedFilter: filter,
                requestedRangeMode: rangeMode))
        {
            return false;
        }

        currentPage = 1;
        await ReloadFlightsAsync();
        return true;
    }

    private async Task RefreshEverythingAsync()
    {
        if (isRefreshing)
            return;

        if (await LoadDashboardAsync(lifetimeCts.Token))
            await ReloadFlightsAsync();
    }

    private async Task<bool> LoadDashboardAsync(
        CancellationToken cancellationToken,
        bool includeOptions = true,
        DashboardFilter? requestedFilter = null,
        DashboardRangeMode? requestedRangeMode = null)
    {
        dashboardRequestCts?.Cancel();
        dashboardRequestCts?.Dispose();
        dashboardRequestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var requestToken = dashboardRequestCts.Token;
        var filter = requestedFilter ?? appliedFilter;

        isRefreshing = true;
        if (dashboard is null)
            isInitialLoading = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            var result = await Operations.GetOperationsDashboardAsync(
                filter.FromUtc,
                filter.ToUtc,
                filter.StationIds,
                filter.CustomerIds,
                filter.ServiceIds,
                includeOptions,
                requestToken);

            if (requestToken.IsCancellationRequested)
                return false;

            if (requestedFilter is not null)
            {
                appliedFilter = filter;
                activeRangeMode = requestedRangeMode ?? activeRangeMode;
            }
            dashboard = result;
            if (includeOptions)
            {
                stationOptions = result.StationOptions;
                customerOptions = result.CustomerOptions;
                serviceOptions = result.ServiceOptions;
            }
            dashboardLoadError = false;
            return true;
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            // A newer filter or page disposal superseded this request.
            return false;
        }
        catch (ApiException) when (!requestToken.IsCancellationRequested)
        {
            dashboardLoadError = true;
            return false;
        }
        catch (JSException) when (!requestToken.IsCancellationRequested)
        {
            dashboardLoadError = true;
            return false;
        }
        finally
        {
            if (!requestToken.IsCancellationRequested)
            {
                isRefreshing = false;
                isInitialLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private async Task LoadFlightsAsync(LoadDataArgs args)
    {
        currentPageSize = args.Top ?? currentPageSize;
        currentPage = ((args.Skip ?? 0) / Math.Max(currentPageSize, 1)) + 1;
        currentSort = SortBuilder.From(args);

        tableRequestCts?.Cancel();
        tableRequestCts?.Dispose();
        tableRequestCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
        var requestToken = tableRequestCts.Token;

        isTableLoading = true;
        tableLoadError = false;
        await InvokeAsync(StateHasChanged);

        try
        {
            var result = await Operations.GetOperationsDashboardFlightsAsync(
                currentPage,
                currentPageSize,
                appliedFilter.FromUtc,
                appliedFilter.ToUtc,
                appliedFilter.StationIds,
                appliedFilter.CustomerIds,
                appliedFilter.ServiceIds,
                currentSort,
                requestToken);

            if (requestToken.IsCancellationRequested)
                return;

            flightRows = result.Items;
            flightTotalCount = result.TotalCount;
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            // A newer paging/filter request superseded this request.
        }
        catch (ApiException) when (!requestToken.IsCancellationRequested)
        {
            tableLoadError = true;
        }
        catch (JSException) when (!requestToken.IsCancellationRequested)
        {
            tableLoadError = true;
        }
        finally
        {
            if (!requestToken.IsCancellationRequested)
            {
                isTableLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private Task ReloadFlightsAsync() =>
        flightList?.ReloadAsync() ?? LoadFlightsAsync(new LoadDataArgs { Skip = 0, Top = currentPageSize });

    private async Task OnPageSizeChangedAsync(int pageSize)
    {
        currentPageSize = pageSize;
        await GridPrefs.SetPageSizeAsync(GridKey, pageSize);
    }

    private async Task ExportAsync(string format)
    {
        if (isExporting || flightTotalCount <= 0)
            return;

        isExporting = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            await Operations.ExportOperationsDashboardFlightsAsync(
                format,
                appliedFilter.FromUtc,
                appliedFilter.ToUtc,
                appliedFilter.StationIds,
                appliedFilter.CustomerIds,
                appliedFilter.ServiceIds,
                currentSort,
                lifetimeCts.Token);

            Notifications.Notify(
                NotificationSeverity.Success,
                UiStrings.OperationsDashboard.ExportReady,
                UiStrings.OperationsDashboard.ExportReadyDescription);
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
            // The page was left while the browser download was starting.
        }
        catch (ApiException ex)
        {
            Notifications.Notify(
                NotificationSeverity.Error,
                UiStrings.OperationsDashboard.ExportFailed,
                ex.ToDisplayMessage(UiStrings.OperationsDashboard.ExportFailedDescription));
        }
        catch (JSException)
        {
            Notifications.Notify(
                NotificationSeverity.Error,
                UiStrings.OperationsDashboard.ExportFailed,
                UiStrings.OperationsDashboard.ExportFailedDescription);
        }
        finally
        {
            isExporting = false;
            if (!lifetimeCts.IsCancellationRequested)
                await InvokeAsync(StateHasChanged);
        }
    }

    private DashboardFilter BuildFilter(DashboardRangeMode mode, DateTime fromDate, DateTime toDate)
    {
        var normalizedFrom = fromDate.Date;
        var normalizedTo = mode == DashboardRangeMode.Period ? toDate.Date : normalizedFrom;
        return new DashboardFilter(
            UtcDayBoundary(normalizedFrom),
            UtcDayBoundary(normalizedTo.AddDays(1)),
            normalizedFrom,
            normalizedTo,
            SelectedIds(selectedStationIds),
            SelectedIds(selectedCustomerIds),
            SelectedIds(selectedServiceIds));
    }

    private static DateTimeOffset UtcDayBoundary(DateTime date) =>
        new(DateTime.SpecifyKind(date.Date, DateTimeKind.Utc));

    private async void OnAuthStateChanged()
    {
        try
        {
            if (Auth.Status == AuthStatus.Authenticated)
                TryStartPolling();
            else if (Auth.Status == AuthStatus.Anonymous)
                lifetimeCts.Cancel();

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            await DispatchExceptionAsync(ex);
        }
    }

    private string CalendarModeClass(DashboardRangeMode mode) =>
        selectedRangeMode == mode ? "od-calendar-mode is-active" : "od-calendar-mode";

    private static IReadOnlyList<Guid> SelectedIds(IEnumerable<Guid>? values) =>
        values?.Distinct().ToList() ?? [];

    private static string FormatCount(long? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
    private static string FormatPercentage(double value) => value.ToString("0.#", CultureInfo.CurrentCulture) + "%";
    private static string SvgNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string DisplayCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim().ToUpperInvariant();

    private static string DisplayFlightNumber(DashboardFlightRow flight) =>
        string.IsNullOrWhiteSpace(flight.CustomerIataCode)
            ? flight.FlightNumber
            : $"{flight.CustomerIataCode.Trim().ToUpperInvariant()}-{flight.FlightNumber}";

    private static string DateTimeDisplay(DateTimeOffset value) =>
        value.UtcDateTime.ToString("dd MMM yyyy · HH:mm", CultureInfo.CurrentCulture);

    private static string StatusLabel(string status) => status switch
    {
        "InProgress" => UiStrings.Dashboard.InProgress,
        "Completed" => UiStrings.Dashboard.Completed,
        "Canceled" => UiStrings.Dashboard.Canceled,
        _ => UiStrings.Dashboard.Scheduled
    };

    private static string StatusTone(string status) => status switch
    {
        "Completed" => "success",
        "Canceled" => "danger",
        "InProgress" => "warning",
        "Scheduled" => "info",
        _ => "neutral"
    };

    public async ValueTask DisposeAsync()
    {
        Auth.StateChanged -= OnAuthStateChanged;
        lifetimeCts.Cancel();
        dashboardRequestCts?.Cancel();
        tableRequestCts?.Cancel();
        filterAutoApplyCts?.Cancel();

        if (pollingTask is not null)
            await pollingTask;

        dashboardRequestCts?.Dispose();
        tableRequestCts?.Dispose();
        filterAutoApplyCts?.Dispose();
        lifetimeCts.Dispose();
    }

    private sealed record DashboardFilter(
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        DateTime FromDate,
        DateTime ToDate,
        IReadOnlyList<Guid> StationIds,
        IReadOnlyList<Guid> CustomerIds,
        IReadOnlyList<Guid> ServiceIds);

    private sealed record StatusSegment(
        string Status,
        string Label,
        string Tone,
        long Count,
        double Percentage,
        double Offset);

    private enum DashboardRangeMode
    {
        SingleDay,
        Period,
        Live
    }
}
