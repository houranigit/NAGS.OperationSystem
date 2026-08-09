using System.Globalization;
using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Pages;

public partial class Dashboard : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetimeCts = new();
    private OperationsDashboard? flightSummary;
    private long? workOrderCount;
    private DateTimeOffset? snapshotGeneratedAtUtc;
    private bool hasAttemptedSummaryLoad;
    private bool isSummaryLoading = true;
    private bool summaryHasError;

    [Inject] private AuthSession Auth { get; set; } = default!;
    [Inject] private OperationsApiClient Operations { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private UserTimeZone UserTimeZone { get; set; } = default!;

    private bool CanViewFlightSummary => Auth.HasPermission(OperationsPermissions.DashboardView);
    private bool CanViewWorkOrders => Auth.HasPermission(OperationsPermissions.WorkOrdersView);
    private bool HasSummaryAccess => CanViewFlightSummary || CanViewWorkOrders;
    private bool CanAccessRootDashboard =>
        !Auth.IsViewerOnly || Auth.HasPermission(OperationsPermissions.DashboardView);

    private int SummaryPlaceholderCount => CanViewFlightSummary ? (CanViewWorkOrders ? 5 : 4) : 1;

    private string FirstName => Auth.User?.DisplayName
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault() ?? Auth.User?.DisplayName ?? string.Empty;

    private string TodayLabel => UserTimeZone.ToLocal(DateTimeOffset.UtcNow)
        .ToString("dddd, MMMM d", CultureInfo.CurrentCulture);

    private IReadOnlyList<DashboardFeature> FeatureCards =>
        DashboardFeatureCatalog.BuildFeatures(Auth.User?.Permissions ?? []);

    private IReadOnlyList<DashboardAction> QuickActions =>
        DashboardFeatureCatalog.BuildQuickActions(Auth.User?.Permissions ?? []);

    private IReadOnlyList<DashboardSummaryMetric> SummaryMetrics
    {
        get
        {
            var metrics = new List<DashboardSummaryMetric>(5);
            if (CanViewFlightSummary)
            {
                metrics.Add(new("scheduled", "Scheduled flights", "Planned", "event_upcoming", "info", flightSummary?.ScheduledFlights));
                metrics.Add(new("in-progress", "In progress", "Under way", "flight_takeoff", "warning", flightSummary?.InProgressFlights));
                metrics.Add(new("completed", "Completed flights", "Recorded", "task_alt", "success", flightSummary?.CompletedFlights));
                metrics.Add(new("canceled", "Canceled flights", "Recorded", "event_busy", "danger", flightSummary?.CanceledFlights));
            }

            if (CanViewWorkOrders)
                metrics.Add(new("work-orders", "Work orders", "All visible records", "assignment", "primary", workOrderCount));

            return metrics;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        Auth.StateChanged += OnAuthStateChanged;
        try
        {
            await UserTimeZone.InitializeAsync();
            if (!RedirectViewerFromUnavailableDashboard())
                await LoadSummaryOnceAsync(lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
            // Expected when the user navigates away while the snapshot is loading.
        }
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

    private async Task LoadSummaryOnceAsync(CancellationToken cancellationToken)
    {
        if (hasAttemptedSummaryLoad ||
            Auth.Status != AuthStatus.Authenticated ||
            cancellationToken.IsCancellationRequested)
        {
            return;
        }

        hasAttemptedSummaryLoad = true;
        if (!HasSummaryAccess)
        {
            isSummaryLoading = false;
            return;
        }

        isSummaryLoading = true;
        var loaders = new List<Task<bool>>(2);
        if (CanViewFlightSummary)
            loaders.Add(LoadFlightSummaryAsync(cancellationToken));
        if (CanViewWorkOrders)
            loaders.Add(LoadWorkOrderCountAsync(cancellationToken));

        var results = await Task.WhenAll(loaders);
        summaryHasError = results.Any(success => !success);
        snapshotGeneratedAtUtc = flightSummary?.GeneratedAtUtc
            ?? (results.Any(success => success) ? DateTimeOffset.UtcNow : null);
        isSummaryLoading = false;
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

    private async Task<bool> LoadWorkOrderCountAsync(CancellationToken cancellationToken)
    {
        try
        {
            workOrderCount = (await Operations.GetWorkOrdersAsync(1, 1, ct: cancellationToken)).TotalCount;
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
            if (Auth.Status == AuthStatus.Anonymous)
            {
                lifetimeCts.Cancel();
                return;
            }

            if (Auth.Status == AuthStatus.Authenticated && !RedirectViewerFromUnavailableDashboard())
                await LoadSummaryOnceAsync(lifetimeCts.Token);

            await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
        {
            // Expected when the user signs out or navigates away while the snapshot is loading.
        }
        catch (Exception ex)
        {
            await DispatchExceptionAsync(ex);
        }
    }

    private void NavigateTo(string href) => Navigation.NavigateTo(href);

    private static string QuickActionClass(bool isPrimary) =>
        isPrimary ? "os-home-quick-action os-home-quick-action--primary" : "os-home-quick-action";

    private static string SummaryCardClass(string tone) => $"os-home-summary-card os-home-summary-card--{tone}";
    private static string FeatureIconClass(string tone) => $"os-home-feature-icon os-home-feature-icon--{tone}";
    private static string FormatCount(long? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
    private string SnapshotTime(DateTimeOffset value) =>
        $"Snapshot {UserTimeZone.ToLocal(value):HH:mm} {UserTimeZone.Id}";

    public ValueTask DisposeAsync()
    {
        Auth.StateChanged -= OnAuthStateChanged;
        lifetimeCts.Cancel();
        lifetimeCts.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed record DashboardSummaryMetric(
    string Key,
    string Label,
    string Hint,
    string Icon,
    string Tone,
    long? Value);

internal sealed record DashboardFeature(
    string Key,
    string Title,
    string Description,
    string Icon,
    string Tone,
    string Href,
    string ActionLabel);

internal sealed record DashboardAction(
    string Key,
    string Label,
    string Icon,
    string Href,
    bool IsPrimary = false);

internal static class DashboardFeatureCatalog
{
    private static readonly IReadOnlyList<(string Permission, string Path, string Label)> MasterDataDestinations =
    [
        (MasterDataPermissions.CountriesView, "/master-data/countries", "countries"),
        (MasterDataPermissions.ManpowerTypesView, "/master-data/manpower-types", "manpower types"),
        (MasterDataPermissions.LicensesView, "/master-data/licenses", "licenses"),
        (MasterDataPermissions.ServicesView, "/master-data/services", "services"),
        (MasterDataPermissions.OperationTypesView, "/master-data/operation-types", "operation types"),
        (MasterDataPermissions.AircraftTypesView, "/master-data/aircraft-types", "aircraft types"),
        (MasterDataPermissions.ToolsView, "/master-data/tools", "tools"),
        (MasterDataPermissions.MaterialsView, "/master-data/materials", "materials"),
        (MasterDataPermissions.GeneralSupportsView, "/master-data/general-supports", "general support"),
        (MasterDataPermissions.StationsView, "/master-data/stations", "stations"),
        (MasterDataPermissions.CustomersView, "/master-data/customers", "customers"),
        (MasterDataPermissions.StaffMembersView, "/master-data/staff-members", "staff members")
    ];

    public static IReadOnlyList<DashboardFeature> BuildFeatures(IEnumerable<string> permissions)
    {
        var granted = permissions.ToHashSet(StringComparer.Ordinal);
        var features = new List<DashboardFeature>();

        AddIfGranted(features, granted, OperationsPermissions.DashboardAnalyticsView,
            new("operations-dashboard", "Operations dashboard", "Open the dedicated live operational view and analytics.", "monitoring", "primary", "/operations/dashboard", "Open operations dashboard"));

        AddIfGranted(features, granted, OperationsPermissions.FlightsView,
            new("flights", "Flights and calendar", "Review schedules, flight details, assignments, and the calendar.", "flight_takeoff", "info", "/operations/flights", "View flights"));

        AddIfGranted(features, granted, OperationsPermissions.WorkOrdersView,
            new("work-orders", "Work orders", "Create, review, or approve work orders according to your access.", "assignment", "success", "/operations/work-orders", "View work orders"));

        AddIfGranted(features, granted, MasterDataPermissions.StaffAllocationView,
            new("staff-allocation", "Staff allocation", "Review station coverage and employee allocation.", "groups", "violet", "/operations/staff-allocation", "Open staff allocation"));

        var administration = new List<(string Path, string Label)>();
        if (granted.Contains(IdentityPermissions.UsersView))
            administration.Add(("/users", "users"));
        if (granted.Contains(IdentityPermissions.RolesView))
            administration.Add(("/roles", "roles"));
        if (granted.Contains(AuditPermissions.TrailsView))
            administration.Add(("/audit", "audit trail"));
        if (administration.Count > 0)
        {
            features.Add(new DashboardFeature(
                "administration",
                "Administration",
                $"Access {JoinLabels(administration.Select(area => area.Label))}.",
                "admin_panel_settings",
                "warning",
                administration[0].Path,
                "Open administration"));
        }

        var masterData = MasterDataDestinations
            .Where(destination => granted.Contains(destination.Permission))
            .ToArray();
        if (masterData.Length > 0)
        {
            var masterDataDescription = masterData.Length <= 3
                ? $"Access {JoinLabels(masterData.Select(area => area.Label))}."
                : $"Access {masterData.Length} reference catalogs, including {JoinLabels(masterData.Take(3).Select(area => area.Label))}.";
            features.Add(new DashboardFeature(
                "master-data",
                "Master data",
                masterDataDescription,
                "database",
                "neutral",
                masterData[0].Path,
                "Open master data"));
        }

        features.Add(new DashboardFeature(
            "account",
            "My account",
            "Review your profile, security settings, and active sessions.",
            "manage_accounts",
            "neutral",
            "/account",
            "Open account"));

        return features;
    }

    public static IReadOnlyList<DashboardAction> BuildQuickActions(IEnumerable<string> permissions)
    {
        var granted = permissions.ToHashSet(StringComparer.Ordinal);
        var actions = new List<DashboardAction>(3);

        if (granted.Contains(OperationsPermissions.FlightsView) &&
            granted.Contains(OperationsPermissions.FlightsSchedule))
            actions.Add(new("schedule-flight", "Schedule a flight", "add", "/operations/flights", true));
        if (granted.Contains(OperationsPermissions.WorkOrdersView) &&
            granted.Contains(OperationsPermissions.WorkOrdersAuthor))
            actions.Add(new("create-work-order", "Create a work order", "add_task", "/operations/work-orders", actions.Count == 0));
        if (granted.Contains(OperationsPermissions.WorkOrdersView) &&
            granted.Contains(OperationsPermissions.WorkOrdersApprove))
            actions.Add(new("review-work-orders", "Review work orders", "fact_check", "/operations/work-orders", actions.Count == 0));
        if (granted.Contains(MasterDataPermissions.StaffAllocationView) &&
            granted.Contains(MasterDataPermissions.StaffAllocationReassign))
            actions.Add(new("allocate-staff", "Allocate staff", "group_add", "/operations/staff-allocation", actions.Count == 0));

        if (actions.Count == 0)
            actions.Add(new("account", "Manage my account", "manage_accounts", "/account", true));

        return actions.Take(3).ToArray();
    }

    private static void AddIfGranted(
        ICollection<DashboardFeature> features,
        IReadOnlySet<string> granted,
        string permission,
        DashboardFeature feature)
    {
        if (granted.Contains(permission))
            features.Add(feature);
    }

    private static string JoinLabels(IEnumerable<string> labels)
    {
        var values = labels.ToArray();
        return values.Length switch
        {
            0 => "available records",
            1 => values[0],
            2 => $"{values[0]} and {values[1]}",
            _ => $"{string.Join(", ", values[..^1])}, and {values[^1]}"
        };
    }
}
