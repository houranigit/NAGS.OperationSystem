using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Features.Operations.Components;
using OperationsSystem.Blazor.Client.Localization;
using OperationsSystem.Blazor.Client.Shared;
using OperationsSystem.Blazor.Client.State;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.Operations.Pages;

public partial class OperationsDashboardPage : IAsyncDisposable
{
    private const string GridKey = "operations-dashboard-flights";
    private const int DashboardTopCount = 4;
    private static readonly TimeSpan FilterAutoApplyDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RealtimeCoalesceDelay = TimeSpan.FromMilliseconds(180);
    private static readonly int[] PageSizes = [10, 25, 50, 100];
    private static DateTime UtcToday => DateTime.UtcNow.Date;

    private static readonly IReadOnlyList<string> StationFills =
        ["#2f6fed", "#0f9f8f", "#8a1538", "#f59e0b", "#7c3aed", "#e05263", "#0891b2", "#64748b"];
    private static readonly IReadOnlyList<string> LandingModeFills = ["#0f9f8f", "#2f6fed"];
    private static readonly IReadOnlyList<string> OperationTypeFills =
        ["#8a1538", "#7c3aed", "#2f6fed", "#0f9f8f", "#f59e0b", "#64748b"];
    private static readonly IReadOnlyList<string> CustomerFills =
        ["#8a1538", "#2f6fed", "#0f9f8f", "#f59e0b", "#94a3b8"];
    private static readonly IReadOnlyList<string> ServiceFills =
        ["#d97706", "#8a1538", "#2f6fed", "#0f9f8f", "#94a3b8"];

    private readonly CancellationTokenSource lifetimeCts = new();
    private CancellationTokenSource? dashboardRequestCts;
    private CancellationTokenSource? tableRequestCts;
    private CancellationTokenSource? filterAutoApplyCts;
    private CancellationTokenSource? realtimeRefreshCts;
    private Task? initializationTask;
    private Task? utcRolloverTask;
    private DataListCard<DashboardFlightRow>? flightList;

    private OperationsDashboard? dashboard;
    private IReadOnlyList<DashboardFlightRow> flightRows = [];
    private IReadOnlyList<DashboardFilterOption> stationOptions = [];
    private IReadOnlyList<DashboardFilterOption> customerOptions = [];
    private IReadOnlyList<DashboardFilterOption> serviceOptions = [];

    private DashboardDateMode dateMode = DashboardDateMode.Day;
    private DashboardPeriodPreset selectedPreset = DashboardPeriodPreset.Today;
    private DateTime? selectedDay;
    private DateTime? selectedFromDate;
    private DateTime? selectedToDate;
    private IEnumerable<Guid>? selectedStationIds = [];
    private IEnumerable<Guid>? selectedCustomerIds = [];
    private IEnumerable<Guid>? selectedServiceIds = [];
    private DashboardFilter appliedFilter = default!;
    private DashboardFilter displayedFilter = default!;

    private long flightTotalCount;
    private int currentPageSize = 10;
    private string? currentSort;
    private bool isInitialLoading = true;
    private bool isRefreshing;
    private bool isFilterApplyPending;
    private bool isFilterRefreshActive;
    private bool isTableLoading;
    private bool isExporting;
    private Guid? printingWorkOrderFlightId;
    private bool dashboardLoadError;
    private bool tableLoadError;
    private bool isRealtimeConnected;
    private bool hasConnectedOnce;
    private long filterRevision;
    private long refreshSequence;
    private long activeRefreshSequence;

    [Inject] private AuthSession Auth { get; set; } = default!;
    [Inject] private OperationsApiClient Operations { get; set; } = default!;
    [Inject] private OperationsDashboardRealtimeClient Realtime { get; set; } = default!;
    [Inject] private NotificationService Notifications { get; set; } = default!;
    [Inject] private GridPreferences GridPrefs { get; set; } = default!;

    private int FlightTotalCount => flightTotalCount > int.MaxValue ? int.MaxValue : (int)flightTotalCount;
    private bool CanExport => Auth.HasPermission(OperationsPermissions.DashboardExport);
    private bool IsFilterBusy => isFilterApplyPending || isFilterRefreshActive;
    private string FilterBarClass => IsFilterBusy
        ? "od-filter-bar is-updating"
        : "od-filter-bar";
    private bool HasActiveDimensionFilters =>
        SelectedIds(selectedStationIds).Count > 0 ||
        SelectedIds(selectedCustomerIds).Count > 0 ||
        SelectedIds(selectedServiceIds).Count > 0;
    private string RealtimeClass => isRealtimeConnected
        ? "od-live-state is-connected"
        : "od-live-state is-connecting";
    private string RealtimeLabel => isRealtimeConnected
        ? UiStrings.OperationsDashboard.LiveConnected
        : UiStrings.OperationsDashboard.LiveConnecting;
    private string LastUpdatedLabel => dashboard is null
        ? "—"
        : dashboard.GeneratedAtUtc.UtcDateTime.ToString("HH:mm:ss 'UTC'", CultureInfo.CurrentCulture);
    private string TrendRangeKey =>
        $"{displayedFilter.FromUtc:O}|{displayedFilter.ToUtc:O}";

    private IReadOnlyList<PeriodOption> PeriodPresets =>
    [
        new(DashboardPeriodPreset.Today, UiStrings.OperationsDashboard.Today),
        new(DashboardPeriodPreset.LastMonth, UiStrings.OperationsDashboard.LastMonth),
        new(DashboardPeriodPreset.LastThreeMonths, UiStrings.OperationsDashboard.LastThreeMonths),
        new(DashboardPeriodPreset.Max, UiStrings.OperationsDashboard.Max)
    ];

    private string FilterRangeSummary => FormatRangeSummary(appliedFilter);
    private string RangeSummary => FormatRangeSummary(displayedFilter);

    private IReadOnlyList<StatusCard> StatusCards =>
        dashboard?.Statuses.Select(item => new StatusCard(
            item.Status,
            StatusLabel(item.Status),
            StatusTone(item.Status),
            StatusIcon(item.Status),
            item.FlightCount,
            item.Percentage)).ToList() ?? [];

    protected override void OnInitialized()
    {
        var today = UtcToday;
        selectedDay = today;
        selectedFromDate = today;
        selectedToDate = today;
        appliedFilter = BuildFilter(
            UtcDayBoundary(today),
            UtcDayBoundary(today.AddDays(1)),
            today,
            today);
        displayedFilter = appliedFilter;

        Auth.StateChanged += OnAuthStateChanged;
        Realtime.DashboardChanged += OnRealtimeDashboardChanged;
        Realtime.ConnectionStateChanged += OnRealtimeConnectionStateChanged;
        TryStartInitialization();
        utcRolloverTask = RunUtcDayRolloverAsync(lifetimeCts.Token);
    }

    private void TryStartInitialization()
    {
        if (initializationTask is not null ||
            Auth.Status != AuthStatus.Authenticated ||
            !Auth.HasPermission(OperationsPermissions.DashboardAnalyticsView))
        {
            return;
        }

        initializationTask = InitializeAsync(lifetimeCts.Token);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            currentPageSize = await GridPrefs.GetPageSizeAsync(GridKey, currentPageSize, PageSizes);
            if (await LoadDashboardAsync(cancellationToken))
                await ReloadFlightsAsync();

            await Realtime.StartAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the page is left or the authenticated session ends.
        }
        catch (Exception ex)
        {
            await InvokeAsync(() => DispatchExceptionAsync(ex));
        }
    }

    private Task OnStationSelectionChangedAsync(IEnumerable<Guid>? values)
    {
        selectedStationIds = values?.Distinct().ToList() ?? [];
        SetAppliedFilter(appliedFilter with { StationIds = SelectedIds(selectedStationIds) });
        return ScheduleDimensionFilterApplyAsync();
    }

    private Task OnCustomerSelectionChangedAsync(IEnumerable<Guid>? values)
    {
        selectedCustomerIds = values?.Distinct().ToList() ?? [];
        SetAppliedFilter(appliedFilter with { CustomerIds = SelectedIds(selectedCustomerIds) });
        return ScheduleDimensionFilterApplyAsync();
    }

    private Task OnServiceSelectionChangedAsync(IEnumerable<Guid>? values)
    {
        selectedServiceIds = values?.Distinct().ToList() ?? [];
        SetAppliedFilter(appliedFilter with { ServiceIds = SelectedIds(selectedServiceIds) });
        return ScheduleDimensionFilterApplyAsync();
    }

    private async Task ScheduleDimensionFilterApplyAsync()
    {
        filterAutoApplyCts?.Cancel();
        filterAutoApplyCts?.Dispose();
        var requestCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
        filterAutoApplyCts = requestCts;
        var requestToken = requestCts.Token;

        isFilterApplyPending = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            await Task.Delay(FilterAutoApplyDelay, requestToken);
            await RefreshFilteredDataAsync(requestToken, includeOptions: false);
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            // A newer dimension selection superseded this one.
        }
        finally
        {
            if (ReferenceEquals(filterAutoApplyCts, requestCts))
            {
                isFilterApplyPending = false;
                if (!lifetimeCts.IsCancellationRequested)
                    await InvokeAsync(StateHasChanged);
            }
        }
    }

    private async Task ClearDimensionFiltersAsync()
    {
        selectedStationIds = [];
        selectedCustomerIds = [];
        selectedServiceIds = [];
        await ApplyDashboardFilterAsync(appliedFilter with
        {
            StationIds = [],
            CustomerIds = [],
            ServiceIds = []
        }, lifetimeCts.Token);
    }

    private async Task SelectPeriodPresetAsync(DashboardPeriodPreset preset)
    {
        if (isInitialLoading)
            return;

        var filter = ConfigurePeriodPreset(preset);
        if (filter is null)
            return;

        selectedPreset = preset;
        await ApplyDashboardFilterAsync(filter, lifetimeCts.Token);
    }

    private async Task SelectDateModeAsync(DashboardDateMode mode)
    {
        if (mode == dateMode)
            return;

        dateMode = mode;
        selectedPreset = DashboardPeriodPreset.Custom;
        var anchor = selectedDay ?? selectedToDate ?? selectedFromDate ?? UtcToday;

        if (mode == DashboardDateMode.Day)
        {
            selectedDay = anchor;
            selectedFromDate = anchor;
            selectedToDate = anchor;
            await ApplyDashboardFilterAsync(
                BuildFilter(
                    UtcDayBoundary(anchor),
                    UtcDayBoundary(anchor.AddDays(1)),
                    anchor,
                    anchor),
                lifetimeCts.Token);
            return;
        }

        selectedFromDate ??= anchor;
        selectedToDate ??= anchor;
    }

    private async Task OnDayChangedAsync(DateTime? value)
    {
        if (value is null)
            return;

        var day = value.Value.Date;
        selectedPreset = DashboardPeriodPreset.Custom;
        selectedDay = day;
        selectedFromDate = day;
        selectedToDate = day;
        await ApplyDashboardFilterAsync(
            BuildFilter(UtcDayBoundary(day), UtcDayBoundary(day.AddDays(1)), day, day),
            lifetimeCts.Token);
    }

    private Task OnFromDateChangedAsync(DateTime? value)
    {
        if (value is null)
            return Task.CompletedTask;

        selectedFromDate = value.Value.Date;
        if (selectedToDate is null || selectedToDate.Value.Date < selectedFromDate.Value.Date)
            selectedToDate = selectedFromDate;

        return ApplyCustomRangeAsync();
    }

    private Task OnToDateChangedAsync(DateTime? value)
    {
        if (value is null)
            return Task.CompletedTask;

        selectedToDate = value.Value.Date;
        if (selectedFromDate is null || selectedFromDate.Value.Date > selectedToDate.Value.Date)
            selectedFromDate = selectedToDate;

        return ApplyCustomRangeAsync();
    }

    private async Task ApplyCustomRangeAsync()
    {
        if (selectedFromDate is not { } fromDate || selectedToDate is not { } toDate)
            return;

        selectedPreset = DashboardPeriodPreset.Custom;
        selectedDay = toDate;
        await ApplyDashboardFilterAsync(
            BuildFilter(
                UtcDayBoundary(fromDate),
                UtcDayBoundary(toDate.AddDays(1)),
                fromDate,
                toDate),
            lifetimeCts.Token);
    }

    private async Task<bool> ApplyDashboardFilterAsync(
        DashboardFilter filter,
        CancellationToken cancellationToken)
    {
        filterAutoApplyCts?.Cancel();
        isFilterApplyPending = false;
        SetAppliedFilter(filter);
        return await RefreshFilteredDataAsync(cancellationToken, includeOptions: false);
    }

    private async Task RefreshEverythingAsync()
    {
        if (isInitialLoading || IsFilterBusy || isRefreshing)
            return;

        RefreshRollingPresetState();
        await RefreshFilteredDataAsync(lifetimeCts.Token, includeOptions: true);
    }

    private async Task<bool> LoadDashboardAsync(
        CancellationToken cancellationToken,
        bool includeOptions = true)
    {
        dashboardRequestCts?.Cancel();
        dashboardRequestCts?.Dispose();
        var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        dashboardRequestCts = requestCts;
        var requestToken = requestCts.Token;
        var filter = appliedFilter;
        var requestRevision = filterRevision;

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
                topCount: DashboardTopCount,
                includeOptions: includeOptions,
                ct: requestToken);

            if (requestToken.IsCancellationRequested || requestRevision != filterRevision)
                return false;

            dashboard = result;
            displayedFilter = filter;
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
            if (ReferenceEquals(dashboardRequestCts, requestCts))
            {
                isRefreshing = false;
                isInitialLoading = false;
                if (!lifetimeCts.IsCancellationRequested)
                    await InvokeAsync(StateHasChanged);
            }
        }
    }

    private async Task LoadFlightsAsync(LoadDataArgs args)
    {
        currentPageSize = args.Top ?? currentPageSize;
        currentSort = SortBuilder.From(args);
        var requestRevision = filterRevision;
        var filter = displayedFilter;

        tableRequestCts?.Cancel();
        tableRequestCts?.Dispose();
        var requestCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
        tableRequestCts = requestCts;
        var requestToken = requestCts.Token;

        isTableLoading = true;
        tableLoadError = false;
        await InvokeAsync(StateHasChanged);

        try
        {
            var page = ((args.Skip ?? 0) / Math.Max(currentPageSize, 1)) + 1;
            var result = await Operations.GetOperationsDashboardFlightsAsync(
                page,
                currentPageSize,
                filter.FromUtc,
                filter.ToUtc,
                filter.StationIds,
                filter.CustomerIds,
                filter.ServiceIds,
                currentSort,
                requestToken);

            if (requestToken.IsCancellationRequested || requestRevision != filterRevision)
                return;

            flightRows = result.Items;
            flightTotalCount = result.TotalCount;
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            // A newer paging or filter request superseded this request.
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
            if (ReferenceEquals(tableRequestCts, requestCts))
            {
                isTableLoading = false;
                if (!lifetimeCts.IsCancellationRequested)
                    await InvokeAsync(StateHasChanged);
            }
        }
    }

    private Task ReloadFlightsAsync() =>
        flightList?.ReloadAsync() ??
        LoadFlightsAsync(new LoadDataArgs { Skip = 0, Top = currentPageSize });

    private async Task OnPageSizeChangedAsync(int pageSize)
    {
        currentPageSize = pageSize;
        await GridPrefs.SetPageSizeAsync(GridKey, pageSize);
    }

    private async Task ExportAsync(string format)
    {
        if (!CanExport ||
            IsFilterBusy ||
            isTableLoading ||
            tableLoadError ||
            isExporting ||
            flightTotalCount <= 0)
        {
            return;
        }

        isExporting = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            await Operations.ExportOperationsDashboardFlightsAsync(
                format,
                displayedFilter.FromUtc,
                displayedFilter.ToUtc,
                displayedFilter.StationIds,
                displayedFilter.CustomerIds,
                displayedFilter.ServiceIds,
                currentSort,
                lifetimeCts.Token);

            Notifications.Notify(
                NotificationSeverity.Success,
                UiStrings.OperationsDashboard.ExportReady,
                UiStrings.OperationsDashboard.ExportReadyDescription);
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
            // The page was left while the download was starting.
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

    private async Task PrintWorkOrderAsync(DashboardFlightRow flight)
    {
        if (printingWorkOrderFlightId.HasValue || !CanPrintWorkOrder(flight))
            return;

        printingWorkOrderFlightId = flight.Id;
        try
        {
            await Operations.DownloadDashboardApprovedWorkOrderAsync(flight.Id, lifetimeCts.Token);
            Notifications.Notify(
                NotificationSeverity.Success,
                UiStrings.Flights.WorkOrderDownloadReady,
                UiStrings.Flights.WorkOrderDownloadReadyDetail);
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
            // The page was left while the download was starting.
        }
        catch (ApiException ex)
        {
            Notifications.Notify(
                NotificationSeverity.Error,
                UiStrings.Flights.WorkOrderDownloadFailed,
                ex.ToDisplayMessage(UiStrings.Flights.WorkOrderDownloadFailedDetail));
        }
        catch (JSException)
        {
            Notifications.Notify(
                NotificationSeverity.Error,
                UiStrings.Flights.WorkOrderDownloadFailed,
                UiStrings.Flights.WorkOrderDownloadFailedDetail);
        }
        finally
        {
            if (printingWorkOrderFlightId == flight.Id)
                printingWorkOrderFlightId = null;
        }
    }

    private async void OnRealtimeDashboardChanged()
    {
        try
        {
            await InvokeAsync(ScheduleRealtimeRefreshAsync);
        }
        catch (Exception ex)
        {
            await InvokeAsync(() => DispatchExceptionAsync(ex));
        }
    }

    private async void OnRealtimeConnectionStateChanged(bool connected)
    {
        try
        {
            await InvokeAsync(async () =>
            {
                var shouldReconcile = connected && hasConnectedOnce;
                isRealtimeConnected = connected;
                if (connected)
                    hasConnectedOnce = true;

                StateHasChanged();
                if (shouldReconcile)
                    await ScheduleRealtimeRefreshAsync();
            });
        }
        catch (Exception ex)
        {
            await InvokeAsync(() => DispatchExceptionAsync(ex));
        }
    }

    private async Task ScheduleRealtimeRefreshAsync()
    {
        realtimeRefreshCts?.Cancel();
        realtimeRefreshCts?.Dispose();
        realtimeRefreshCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
        var requestToken = realtimeRefreshCts.Token;

        try
        {
            await Task.Delay(RealtimeCoalesceDelay, requestToken);
            RefreshRollingPresetState();
            await RefreshFilteredDataAsync(requestToken, includeOptions: true);
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            // A newer invalidation coalesced this refresh.
        }
    }

    private async void OnAuthStateChanged()
    {
        try
        {
            await InvokeAsync(async () =>
            {
                if (Auth.Status == AuthStatus.Authenticated)
                {
                    TryStartInitialization();
                }
                else if (Auth.Status == AuthStatus.Anonymous)
                {
                    lifetimeCts.Cancel();
                    await Realtime.StopAsync();
                }

                StateHasChanged();
            });
        }
        catch (Exception ex)
        {
            await InvokeAsync(() => DispatchExceptionAsync(ex));
        }
    }

    private async Task<bool> RefreshFilteredDataAsync(
        CancellationToken cancellationToken,
        bool includeOptions)
    {
        var refreshId = ++refreshSequence;
        activeRefreshSequence = refreshId;
        isFilterRefreshActive = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            if (!await LoadDashboardAsync(cancellationToken, includeOptions))
                return false;

            await ReloadFlightsAsync();
            return true;
        }
        finally
        {
            if (activeRefreshSequence == refreshId)
            {
                isFilterRefreshActive = false;
                if (!lifetimeCts.IsCancellationRequested)
                    await InvokeAsync(StateHasChanged);
            }
        }
    }

    private void SetAppliedFilter(DashboardFilter filter)
    {
        appliedFilter = filter;
        filterRevision++;
        dashboardRequestCts?.Cancel();
        tableRequestCts?.Cancel();
    }

    private DashboardFilter? ConfigurePeriodPreset(DashboardPeriodPreset preset)
    {
        var today = UtcToday;
        switch (preset)
        {
            case DashboardPeriodPreset.Today:
                dateMode = DashboardDateMode.Day;
                selectedDay = today;
                selectedFromDate = today;
                selectedToDate = today;
                return BuildFilter(
                    UtcDayBoundary(today),
                    UtcDayBoundary(today.AddDays(1)),
                    today,
                    today);
            case DashboardPeriodPreset.LastMonth:
                dateMode = DashboardDateMode.Range;
                var thisMonth = new DateTime(today.Year, today.Month, 1);
                var lastMonth = thisMonth.AddMonths(-1);
                selectedFromDate = lastMonth;
                selectedToDate = thisMonth.AddDays(-1);
                selectedDay = selectedToDate;
                return BuildFilter(
                    UtcDayBoundary(lastMonth),
                    UtcDayBoundary(thisMonth),
                    lastMonth,
                    thisMonth.AddDays(-1));
            case DashboardPeriodPreset.LastThreeMonths:
                dateMode = DashboardDateMode.Range;
                var threeMonthsAgo = today.AddMonths(-3);
                selectedFromDate = threeMonthsAgo;
                selectedToDate = today;
                selectedDay = today;
                return BuildFilter(
                    UtcDayBoundary(threeMonthsAgo),
                    UtcDayBoundary(today.AddDays(1)),
                    threeMonthsAgo,
                    today);
            case DashboardPeriodPreset.Max:
                dateMode = DashboardDateMode.Range;
                selectedFromDate = null;
                selectedToDate = null;
                return BuildFilter(null, null, null, null);
            default:
                return null;
        }
    }

    private void RefreshRollingPresetState()
    {
        if (selectedPreset is not (
            DashboardPeriodPreset.Today or
            DashboardPeriodPreset.LastMonth or
            DashboardPeriodPreset.LastThreeMonths))
        {
            return;
        }

        if (ConfigurePeriodPreset(selectedPreset) is { } filter)
            SetAppliedFilter(filter);
    }

    private async Task RunUtcDayRolloverAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                var nextUtcDay = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
                await Task.Delay(nextUtcDay - now + TimeSpan.FromMilliseconds(100), cancellationToken);

                await InvokeAsync(async () =>
                {
                    if (selectedPreset is
                        DashboardPeriodPreset.Today or
                        DashboardPeriodPreset.LastMonth or
                        DashboardPeriodPreset.LastThreeMonths)
                    {
                        RefreshRollingPresetState();
                        await RefreshFilteredDataAsync(cancellationToken, includeOptions: true);
                    }
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the page is left or the authenticated session ends.
        }
    }

    private DashboardFilter BuildFilter(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        DateTime? fromDate,
        DateTime? toDate) =>
        new(
            fromUtc,
            toUtc,
            fromDate,
            toDate,
            SelectedIds(selectedStationIds),
            SelectedIds(selectedCustomerIds),
            SelectedIds(selectedServiceIds));

    private string PeriodPresetClass(DashboardPeriodPreset preset) =>
        preset == selectedPreset ? "od-period-preset is-active" : "od-period-preset";

    private string DateModeClass(DashboardDateMode mode) =>
        mode == dateMode ? "od-date-mode is-active" : "od-date-mode";

    private static DateTimeOffset UtcDayBoundary(DateTime date) =>
        new(DateTime.SpecifyKind(date.Date, DateTimeKind.Utc));

    private static IReadOnlyList<Guid> SelectedIds(IEnumerable<Guid>? values) =>
        values?.Distinct().ToList() ?? [];

    private static string FormatCount(long? value) =>
        value?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
    private static string FormatPercentage(double value) =>
        value.ToString("0.#", CultureInfo.CurrentCulture) + "%";
    private static string DisplayCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim().ToUpperInvariant();
    private static string DisplayFlightNumber(DashboardFlightRow flight) =>
        string.IsNullOrWhiteSpace(flight.CustomerIataCode)
            ? flight.FlightNumber
            : $"{flight.CustomerIataCode.Trim().ToUpperInvariant()}-{flight.FlightNumber}";
    private static string DateTimeDisplay(DateTimeOffset value) =>
        value.UtcDateTime.ToString("dd MMM yyyy · HH:mm", CultureInfo.CurrentCulture);
    private bool CanPrintWorkOrder(DashboardFlightRow flight) =>
        CanExport && flight.Status is "Completed";

    private static string FormatRangeSummary(DashboardFilter filter)
    {
        if (filter.FromDate is null || filter.ToDate is null)
            return UiStrings.OperationsDashboard.AllAvailableHistory;

        return filter.FromDate.Value.Date == filter.ToDate.Value.Date
            ? filter.FromDate.Value.ToString("dddd, dd MMM yyyy", CultureInfo.CurrentCulture)
            : string.Format(
                UiStrings.OperationsDashboard.PeriodFormat,
                filter.FromDate.Value.ToString("dd MMM yyyy", CultureInfo.CurrentCulture),
                filter.ToDate.Value.ToString("dd MMM yyyy", CultureInfo.CurrentCulture));
    }

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

    private static string StatusIcon(string status) => status switch
    {
        "Completed" => "check_circle",
        "Canceled" => "cancel",
        "InProgress" => "pending_actions",
        _ => "schedule"
    };

    public async ValueTask DisposeAsync()
    {
        Auth.StateChanged -= OnAuthStateChanged;
        Realtime.DashboardChanged -= OnRealtimeDashboardChanged;
        Realtime.ConnectionStateChanged -= OnRealtimeConnectionStateChanged;

        lifetimeCts.Cancel();
        dashboardRequestCts?.Cancel();
        tableRequestCts?.Cancel();
        filterAutoApplyCts?.Cancel();
        realtimeRefreshCts?.Cancel();

        await Realtime.StopAsync();
        if (initializationTask is not null)
            await initializationTask;
        if (utcRolloverTask is not null)
            await utcRolloverTask;

        dashboardRequestCts?.Dispose();
        tableRequestCts?.Dispose();
        filterAutoApplyCts?.Dispose();
        realtimeRefreshCts?.Dispose();
        lifetimeCts.Dispose();
    }

    private sealed record DashboardFilter(
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtc,
        DateTime? FromDate,
        DateTime? ToDate,
        IReadOnlyList<Guid> StationIds,
        IReadOnlyList<Guid> CustomerIds,
        IReadOnlyList<Guid> ServiceIds);

    private sealed record StatusCard(
        string Status,
        string Label,
        string Tone,
        string Icon,
        long Count,
        double Percentage);

    private sealed record PeriodOption(DashboardPeriodPreset Value, string Label);

    private enum DashboardDateMode
    {
        Day,
        Range
    }
}
