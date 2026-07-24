using System.Globalization;
using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Localization;

namespace OperationsSystem.Blazor.Client.Pages;

public partial class Dashboard : IAsyncDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly IReadOnlyList<string> ActiveFlightStatuses = ["Scheduled", "InProgress"];

    private readonly CancellationTokenSource lifetimeCts = new();
    private Task? pollingTask;
    private OperationsDashboard? flightSummary;
    private WorkOrderLifecycle? workOrderLifecycle;
    private IReadOnlyList<MasterMetric> masterMetrics = [];
    private IReadOnlyList<FlightListItem> activeFlights = [];
    private IReadOnlyList<WorkOrderListItem> recentWorkOrders = [];
    private DateTimeOffset? lastSuccessfulRefreshUtc;
    private bool isInitialLoading = true;
    private bool isRefreshing;
    private bool liveDataHasError;
    private bool masterDataHasError;

    [Inject] private AuthSession Auth { get; set; } = default!;
    [Inject] private MasterDataApiClient MasterData { get; set; } = default!;
    [Inject] private OperationsApiClient Operations { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private bool CanViewFlightSummary => Auth.HasPermission(OperationsPermissions.DashboardView);
    private bool CanViewFlights => Auth.HasPermission(OperationsPermissions.FlightsView);
    private bool CanScheduleFlights => Auth.HasPermission(OperationsPermissions.FlightsSchedule);
    private bool CanViewWorkOrders => Auth.HasPermission(OperationsPermissions.WorkOrdersView);
    private bool CanAuthorWorkOrders => Auth.HasPermission(OperationsPermissions.WorkOrdersAuthor);

    private bool CanViewCustomers => Auth.HasPermission(MasterDataPermissions.CustomersView);
    private bool CanViewStations => Auth.HasPermission(MasterDataPermissions.StationsView);
    private bool CanViewServices => Auth.HasPermission(MasterDataPermissions.ServicesView);
    private bool CanViewAircraftTypes => Auth.HasPermission(MasterDataPermissions.AircraftTypesView);
    private bool CanViewStaffMembers => Auth.HasPermission(MasterDataPermissions.StaffMembersView);

    private bool HasMasterDataAccess =>
        CanViewCustomers || CanViewStations || CanViewServices || CanViewAircraftTypes || CanViewStaffMembers;

    private bool CanAccessRootDashboard =>
        !Auth.IsViewerOnly || Auth.HasPermission(OperationsPermissions.DashboardView);

    private bool HasFlightOperations => CanViewFlightSummary || CanViewFlights;
    private bool HasLiveOperations => HasFlightOperations || CanViewWorkOrders;
    private bool HasDashboardDataAccess => HasMasterDataAccess || HasLiveOperations;
    private bool HasRefreshableData => HasDashboardDataAccess;
    private bool HasDataWarning => liveDataHasError || masterDataHasError;

    private string FirstName => Auth.User?.DisplayName
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault() ?? Auth.User?.DisplayName ?? string.Empty;

    private string TodayLabel => DateTimeOffset.Now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
    private string LiveChipLabel => liveDataHasError ? UiStrings.Dashboard.Stale : UiStrings.Dashboard.Live;
    private string LiveChipClass => $"os-live-chip{(liveDataHasError ? " os-live-chip--stale" : string.Empty)}";
    private string RefreshButtonText => isRefreshing ? UiStrings.Common.Loading : UiStrings.Dashboard.RefreshData;

    private string SyncTitle => HasLiveOperations
        ? LiveChipLabel
        : UiStrings.Dashboard.MasterDataTitle;

    private string SyncDescription => HasLiveOperations
        ? $"{LastRefreshLabel} · {UiStrings.Dashboard.AutoRefresh}"
        : UiStrings.Dashboard.MasterDataDescription;

    private string LastRefreshLabel => lastSuccessfulRefreshUtc is { } refreshedAt
        ? string.Format(UiStrings.Dashboard.UpdatedFormat, RelativeTime(refreshedAt))
        : UiStrings.Dashboard.WaitingForData;

    private string OperationsGridClass => CanViewFlightSummary && CanViewFlights
        ? "os-operations-grid"
        : "os-operations-grid os-operations-grid--single";

    private long TotalFlights => flightSummary is null
        ? 0
        : (long)flightSummary.ScheduledFlights
          + flightSummary.InProgressFlights
          + flightSummary.CompletedFlights
          + flightSummary.CanceledFlights;

    private IReadOnlyList<StatusCard> FlightStatusCards =>
    [
        new("scheduled", UiStrings.Dashboard.Scheduled, UiStrings.Dashboard.ScheduledHint, "event_upcoming", "info", flightSummary?.ScheduledFlights),
        new("in-progress", UiStrings.Dashboard.InProgress, UiStrings.Dashboard.InProgressHint, "flight_takeoff", "warning", flightSummary?.InProgressFlights),
        new("completed", UiStrings.Dashboard.Completed, UiStrings.Dashboard.CompletedHint, "task_alt", "success", flightSummary?.CompletedFlights),
        new("canceled", UiStrings.Dashboard.Canceled, UiStrings.Dashboard.CanceledHint, "event_busy", "danger", flightSummary?.CanceledFlights)
    ];

    private IReadOnlyList<DonutSegment> FlightDonutSegments
    {
        get
        {
            var source = FlightStatusCards;
            var segments = new List<DonutSegment>(source.Count);
            var offset = 0d;

            foreach (var status in source)
            {
                var count = status.Count ?? 0;
                segments.Add(new DonutSegment(status.Key, status.Label, status.Tone, count, offset));
                if (TotalFlights > 0)
                    offset += count * 100d / TotalFlights;
            }

            return segments;
        }
    }

    private IReadOnlyList<StatusCard> WorkOrderStages =>
    [
        new("submitted", UiStrings.Dashboard.Submitted, UiStrings.Dashboard.SubmittedHint, "upload_file", "warning", workOrderLifecycle?.Submitted),
        new("returned", UiStrings.Dashboard.Returned, UiStrings.Dashboard.ReturnedHint, "undo", "danger", workOrderLifecycle?.Returned),
        new("approved", UiStrings.Dashboard.Approved, UiStrings.Dashboard.ApprovedHint, "verified", "success", workOrderLifecycle?.Approved),
        new("merged", UiStrings.Dashboard.Merged, UiStrings.Dashboard.MergedHint, "merge_type", "neutral", workOrderLifecycle?.Merged)
    ];

    protected override void OnInitialized()
    {
        Auth.StateChanged += OnAuthStateChanged;
        if (!RedirectViewerFromUnavailableDashboard())
            TryStartPolling();
    }

    private bool RedirectViewerFromUnavailableDashboard()
    {
        if (Auth.Status != AuthStatus.Authenticated ||
            !Auth.IsViewerOnly ||
            Auth.HasPermission(OperationsPermissions.DashboardView) ||
            Auth.User is not { } user)
        {
            return false;
        }

        Navigation.NavigateTo(
            PortalNavigationPolicy.ResolveLandingPage(user.Permissions),
            replace: true);
        return true;
    }

    private void TryStartPolling()
    {
        if (pollingTask is not null || Auth.Status != AuthStatus.Authenticated || Auth.User is null)
            return;

        pollingTask = PollDashboardAsync(lifetimeCts.Token);
    }

    private async Task PollDashboardAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(includeMasterData: true, cancellationToken);

            var refreshNumber = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(RefreshInterval, cancellationToken);
                refreshNumber++;
                await RefreshAsync(includeMasterData: refreshNumber % 10 == 0, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the user navigates away or the authenticated session ends.
        }
        catch (Exception ex)
        {
            await InvokeAsync(() => DispatchExceptionAsync(ex));
        }
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            await RefreshAsync(includeMasterData: true, lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
            // The user navigated away while a manual refresh was in flight.
        }
    }

    private async Task RefreshAsync(bool includeMasterData, CancellationToken cancellationToken)
    {
        if (isRefreshing || cancellationToken.IsCancellationRequested)
            return;

        isRefreshing = true;
        if (!isInitialLoading)
            await InvokeAsync(StateHasChanged);

        try
        {
            var masterDataTask = includeMasterData
                ? LoadMasterDataAsync(cancellationToken)
                : Task.FromResult(false);
            var liveDataTask = LoadLiveDataAsync(cancellationToken);
            var results = await Task.WhenAll(masterDataTask, liveDataTask);

            if (results[1])
                lastSuccessfulRefreshUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            isRefreshing = false;
            isInitialLoading = false;
            if (!cancellationToken.IsCancellationRequested)
                await InvokeAsync(StateHasChanged);
        }
    }

    private async Task<bool> LoadMasterDataAsync(CancellationToken cancellationToken)
    {
        var loaders = new List<Task<MasterMetric>>();

        if (CanViewCustomers)
        {
            loaders.Add(LoadMasterMetricAsync(
                new MasterMetric("customers", UiStrings.Nav.Customers, "groups", "primary", "/master-data/customers", null),
                async ct => (await MasterData.GetCustomersAsync(1, 1, null, isActive: true, ct: ct)).TotalCount,
                cancellationToken));
        }

        if (CanViewStations)
        {
            loaders.Add(LoadMasterMetricAsync(
                new MasterMetric("stations", UiStrings.Nav.Stations, "flight_takeoff", "info", "/master-data/stations", null),
                async ct => (await MasterData.GetStationsAsync(1, 1, null, isActive: true, ct: ct)).TotalCount,
                cancellationToken));
        }

        if (CanViewServices)
        {
            loaders.Add(LoadMasterMetricAsync(
                new MasterMetric("services", UiStrings.Nav.Services, "settings_suggest", "success", "/master-data/services", null),
                async ct => (await MasterData.GetServicesAsync(1, 1, null, isActive: true, ct: ct)).TotalCount,
                cancellationToken));
        }

        if (CanViewAircraftTypes)
        {
            loaders.Add(LoadMasterMetricAsync(
                new MasterMetric("aircraft-types", UiStrings.Nav.AircraftTypes, "connecting_airports", "warning", "/master-data/aircraft-types", null),
                async ct => (await MasterData.GetAircraftTypesAsync(1, 1, null, isActive: true, ct: ct)).TotalCount,
                cancellationToken));
        }

        if (CanViewStaffMembers)
        {
            loaders.Add(LoadMasterMetricAsync(
                new MasterMetric("staff-members", UiStrings.Nav.StaffMembers, "badge", "violet", "/master-data/staff-members", null),
                async ct => (await MasterData.GetStaffMembersAsync(1, 1, null, isActive: true, ct: ct)).TotalCount,
                cancellationToken));
        }

        if (loaders.Count == 0)
        {
            masterMetrics = [];
            masterDataHasError = false;
            return false;
        }

        var loadedMetrics = await Task.WhenAll(loaders);
        var previousValues = masterMetrics.ToDictionary(metric => metric.Key, metric => metric.Value);
        masterDataHasError = loadedMetrics.Any(metric => metric.Value is null);
        masterMetrics = loadedMetrics
            .Select(metric => metric.Value is null && previousValues.TryGetValue(metric.Key, out var previousValue)
                ? metric with { Value = previousValue }
                : metric)
            .ToList();

        return loadedMetrics.Any(metric => metric.Value is not null);
    }

    private static async Task<MasterMetric> LoadMasterMetricAsync(
        MasterMetric metric,
        Func<CancellationToken, Task<long>> loader,
        CancellationToken cancellationToken)
    {
        try
        {
            return metric with { Value = await loader(cancellationToken) };
        }
        catch (ApiException)
        {
            return metric;
        }
    }

    private async Task<bool> LoadLiveDataAsync(CancellationToken cancellationToken)
    {
        var loaders = new List<Task<bool>>();

        if (CanViewFlightSummary)
            loaders.Add(LoadFlightSummaryAsync(cancellationToken));
        if (CanViewFlights)
            loaders.Add(LoadActiveFlightsAsync(cancellationToken));
        if (CanViewWorkOrders)
            loaders.Add(LoadWorkOrdersAsync(cancellationToken));

        if (loaders.Count == 0)
        {
            liveDataHasError = false;
            return false;
        }

        var results = await Task.WhenAll(loaders);
        liveDataHasError = results.Any(success => !success);
        return results.Any(success => success);
    }

    private async Task<bool> LoadFlightSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            flightSummary = await Operations.GetDashboardAsync(cancellationToken);
            return true;
        }
        catch (ApiException)
        {
            return false;
        }
    }

    private async Task<bool> LoadActiveFlightsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var result = await Operations.GetFlightsAsync(
                1,
                6,
                statuses: ActiveFlightStatuses,
                fromUtc: now.AddHours(-18),
                toUtc: now.AddHours(48),
                sort: "scheduledArrivalUtc:asc",
                ct: cancellationToken);
            activeFlights = result.Items;
            return true;
        }
        catch (ApiException)
        {
            return false;
        }
    }

    private async Task<bool> LoadWorkOrdersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var results = await Task.WhenAll(
                Operations.GetWorkOrdersAsync(1, 1, status: "Submitted", ct: cancellationToken),
                Operations.GetWorkOrdersAsync(1, 1, status: "Returned", ct: cancellationToken),
                Operations.GetWorkOrdersAsync(1, 1, status: "Approved", ct: cancellationToken),
                Operations.GetWorkOrdersAsync(1, 1, status: "Merged", ct: cancellationToken),
                Operations.GetWorkOrdersAsync(1, 5, ct: cancellationToken));

            workOrderLifecycle = new WorkOrderLifecycle(
                results[0].TotalCount,
                results[1].TotalCount,
                results[2].TotalCount,
                results[3].TotalCount);
            recentWorkOrders = results[4].Items;
            return true;
        }
        catch (ApiException)
        {
            return false;
        }
    }

    private async void OnAuthStateChanged()
    {
        try
        {
            if (Auth.Status == AuthStatus.Authenticated && !RedirectViewerFromUnavailableDashboard())
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

    private void NavigateTo(string uri) => Navigation.NavigateTo(uri);
    private void OpenFlights() => Navigation.NavigateTo("/operations/flights");
    private void OpenWorkOrders() => Navigation.NavigateTo("/operations/work-orders");
    private void OpenFlight(Guid id) => Navigation.NavigateTo($"/operations/flights/{id}");

    private static string FormatCount(long? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";

    private string DonutDashArray(long count)
    {
        var percentage = TotalFlights == 0 ? 0 : count * 100d / TotalFlights;
        return FormattableString.Invariant($"{percentage:0.###} {100d - percentage:0.###}");
    }

    private static string DonutDashOffset(double offset) =>
        (-offset).ToString("0.###", CultureInfo.InvariantCulture);

    private static string DisplayFlightNumber(FlightListItem flight) =>
        string.IsNullOrWhiteSpace(flight.CustomerIataCode)
            ? flight.FlightNumber
            : $"{flight.CustomerIataCode.Trim().ToUpperInvariant()}-{flight.FlightNumber}";

    private static string FlightTime(DateTimeOffset value) => value.UtcDateTime.ToString("HH:mm", CultureInfo.CurrentCulture);
    private static string FlightDate(DateTimeOffset value) => value.UtcDateTime.ToString("dd MMM", CultureInfo.CurrentCulture);

    private static string FlightStatusLabel(string status) => status switch
    {
        "InProgress" => UiStrings.Dashboard.InProgress,
        "Completed" => UiStrings.Dashboard.Completed,
        "Canceled" => UiStrings.Dashboard.Canceled,
        _ => UiStrings.Dashboard.Scheduled
    };

    private static string FlightStatusTone(string status) => status switch
    {
        "Completed" => "success",
        "Canceled" or "Merged" => "danger",
        "InProgress" => "warning",
        _ => "neutral"
    };

    private static string DisplayWorkOrderNumber(WorkOrderListItem workOrder) =>
        string.IsNullOrWhiteSpace(workOrder.ApprovalNumber)
            ? workOrder.Id.ToString("N")[..8].ToUpperInvariant()
            : workOrder.ApprovalNumber;

    private static string DisplayWorkOrderFlight(WorkOrderListItem workOrder) =>
        string.IsNullOrWhiteSpace(workOrder.CustomerIataCode)
            ? workOrder.PlannedFlightNumber
            : $"{workOrder.CustomerIataCode.Trim().ToUpperInvariant()}-{workOrder.PlannedFlightNumber}";

    private static string WorkOrderStatusLabel(string status) => status switch
    {
        "Returned" => UiStrings.Dashboard.Returned,
        "Approved" => UiStrings.Dashboard.Approved,
        "Merged" => UiStrings.Dashboard.Merged,
        _ => UiStrings.Dashboard.Submitted
    };

    private static string WorkOrderStatusTone(string status) => status switch
    {
        "Approved" => "success",
        "Submitted" => "warning",
        "Merged" => "danger",
        _ => "neutral"
    };

    private static string WorkOrderTypeIcon(string type) => type == "Cancellation" ? "cancel" : "task_alt";
    private static string WorkOrderTypeTone(string type) => type == "Cancellation" ? "danger" : "success";

    private static string RelativeTime(DateTimeOffset value)
    {
        var elapsed = DateTimeOffset.UtcNow - value.ToUniversalTime();
        if (elapsed < TimeSpan.FromMinutes(1))
            return UiStrings.Common.JustNow;
        if (elapsed < TimeSpan.FromHours(1))
            return string.Format(UiStrings.Common.MinutesAgo, Math.Max(1, (int)elapsed.TotalMinutes));
        if (elapsed < TimeSpan.FromDays(1))
            return string.Format(UiStrings.Common.HoursAgo, Math.Max(1, (int)elapsed.TotalHours));

        return string.Format(UiStrings.Common.DaysAgo, Math.Max(1, (int)elapsed.TotalDays));
    }

    public async ValueTask DisposeAsync()
    {
        Auth.StateChanged -= OnAuthStateChanged;
        lifetimeCts.Cancel();

        if (pollingTask is not null)
            await pollingTask;

        lifetimeCts.Dispose();
    }

    private sealed record MasterMetric(string Key, string Label, string Icon, string Tone, string Href, long? Value);
    private sealed record StatusCard(string Key, string Label, string Hint, string Icon, string Tone, long? Count);
    private sealed record DonutSegment(string Key, string Label, string Tone, long Count, double Offset);
    private sealed record WorkOrderLifecycle(long Submitted, long Returned, long Approved, long Merged);
}
