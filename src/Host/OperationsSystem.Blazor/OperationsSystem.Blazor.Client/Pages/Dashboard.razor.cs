using System.Globalization;
using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Pages;

public partial class Dashboard
{
    [Inject] private AuthSession Auth { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private UserTimeZone UserTimeZone { get; set; } = default!;

    private bool CanAccessRootDashboard => !Auth.IsViewerOnly;
    private string FirstName => Auth.User?.DisplayName
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault() ?? Auth.User?.DisplayName ?? string.Empty;
    private string TodayLabel => UserTimeZone.IsInitialized
        ? UserTimeZone.ToLocal(DateTimeOffset.UtcNow).ToString("dddd, MMMM d", CultureInfo.CurrentCulture)
        : "—";
    private IReadOnlyList<DashboardFeature> FeatureCards =>
        DashboardFeatureCatalog.BuildFeatures(Auth.User?.Permissions ?? []);
    private IReadOnlyList<DashboardAction> QuickActions =>
        DashboardFeatureCatalog.BuildQuickActions(Auth.User?.Permissions ?? []);

    protected override async Task OnInitializedAsync()
    {
        await UserTimeZone.InitializeAsync();
        if (Auth.IsViewerOnly && Auth.User is { } user)
            Navigation.NavigateTo(PortalNavigationPolicy.ResolveLandingPage(user.Permissions), replace: true);
    }

    private void NavigateTo(string href) => Navigation.NavigateTo(href);
    private static string QuickActionClass(bool isPrimary) =>
        isPrimary ? "os-home-quick-action os-home-quick-action--primary" : "os-home-quick-action";
    private static string FeatureIconClass(string tone) => $"os-home-feature-icon os-home-feature-icon--{tone}";
}

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
        (MasterDataPermissions.AtaChaptersView, "/master-data/ata-chapters", "ATA chapters"),
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

        AddIfGranted(features, granted, OperationsPermissions.FlightsView,
            new("flights", "Flights", "Create flights and manage scheduled flights.", "flight_takeoff", "info", "/operations/flights", "View flights"));

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
