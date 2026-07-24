using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Auth;

public sealed record PortalDestination(string Path, string Permission);

/// <summary>
/// Defines the portal's ordered primary destinations and the Viewer Only routing rules.
/// This is deliberately independent of Blazor components so login, layout, and route guards
/// cannot disagree about a user's landing page or sidebar state.
/// </summary>
public static class PortalNavigationPolicy
{
    public const string FallbackPath = "/account";

    public static IReadOnlyList<PortalDestination> OrderedDestinations { get; } =
    [
        new("/", OperationsPermissions.DashboardView),
        new("/operations/dashboard", OperationsPermissions.DashboardAnalyticsView),
        new("/users", IdentityPermissions.UsersView),
        new("/roles", IdentityPermissions.RolesView),
        new("/audit", AuditPermissions.TrailsView),
        new("/operations/calendar", OperationsPermissions.FlightsView),
        new("/operations/flights", OperationsPermissions.FlightsView),
        new("/operations/work-orders", OperationsPermissions.WorkOrdersView),
        new("/operations/staff-allocation", MasterDataPermissions.StaffAllocationView),
        new("/master-data/countries", MasterDataPermissions.CountriesView),
        new("/master-data/manpower-types", MasterDataPermissions.ManpowerTypesView),
        new("/master-data/licenses", MasterDataPermissions.LicensesView),
        new("/master-data/services", MasterDataPermissions.ServicesView),
        new("/master-data/operation-types", MasterDataPermissions.OperationTypesView),
        new("/master-data/aircraft-types", MasterDataPermissions.AircraftTypesView),
        new("/master-data/tools", MasterDataPermissions.ToolsView),
        new("/master-data/materials", MasterDataPermissions.MaterialsView),
        new("/master-data/general-supports", MasterDataPermissions.GeneralSupportsView),
        new("/master-data/stations", MasterDataPermissions.StationsView),
        new("/master-data/customers", MasterDataPermissions.CustomersView),
        new("/master-data/staff-members", MasterDataPermissions.StaffMembersView)
    ];

    public static bool IsViewerOnly(string? userType) =>
        string.Equals(userType, UserTypes.ViewerOnly, StringComparison.Ordinal);

    public static IReadOnlyList<PortalDestination> GetPrimaryDestinations(IEnumerable<string> permissions)
    {
        var granted = permissions.ToHashSet(StringComparer.Ordinal);
        return OrderedDestinations
            .Where(destination => granted.Contains(destination.Permission))
            .ToArray();
    }

    public static string ResolveLandingPage(IEnumerable<string> permissions) =>
        GetPrimaryDestinations(permissions).FirstOrDefault()?.Path ?? FallbackPath;

    public static bool ShouldShowSidebar(string? userType, IEnumerable<string> permissions) =>
        !IsViewerOnly(userType) || GetPrimaryDestinations(permissions).Count >= 2;

    public static string ResolvePostLogin(
        string? userType,
        IEnumerable<string> permissions,
        string? requestedReturnUrl)
    {
        var granted = permissions.ToHashSet(StringComparer.Ordinal);

        if (TryNormalizeLocalReturnUrl(requestedReturnUrl, out var safeReturnUrl) &&
            !IsLoginRoute(safeReturnUrl) &&
            (!IsViewerOnly(userType) || IsRouteAuthorized(safeReturnUrl, granted)))
        {
            return safeReturnUrl;
        }

        return IsViewerOnly(userType)
            ? ResolveLandingPage(granted)
            : "/";
    }

    public static bool IsRouteAuthorized(string route, IEnumerable<string> permissions)
    {
        if (!TryNormalizeLocalReturnUrl(route, out var normalized))
            return false;

        var granted = permissions as IReadOnlySet<string>
            ?? permissions.ToHashSet(StringComparer.Ordinal);
        var path = PathOnly(normalized);

        if (path is "account" or "notifications")
            return true;

        if (path.Length == 0)
            return granted.Contains(OperationsPermissions.DashboardView);
        if (path == "operations/dashboard")
            return granted.Contains(OperationsPermissions.DashboardAnalyticsView);
        if (path == "operations/calendar" || path == "operations/flights")
            return granted.Contains(OperationsPermissions.FlightsView);
        if (IsGuidDetail(path, "operations/flights"))
            return granted.Contains(OperationsPermissions.FlightsView);
        if (path == "operations/flights/per-landing-extract")
        {
            return granted.Contains(OperationsPermissions.FlightsView)
                   && granted.Contains(OperationsPermissions.WorkOrdersApprove);
        }

        if (path == "operations/work-orders")
            return granted.Contains(OperationsPermissions.WorkOrdersView);
        if (path == "operations/staff-allocation")
            return granted.Contains(MasterDataPermissions.StaffAllocationView);

        if (IsListOrGuidDetail(path, "users"))
            return granted.Contains(IdentityPermissions.UsersView);
        if (IsListOrGuidDetail(path, "roles"))
            return granted.Contains(IdentityPermissions.RolesView);
        if (path == "audit")
            return granted.Contains(AuditPermissions.TrailsView);

        return MasterDataRoutePermission(path) is { } permission && granted.Contains(permission);
    }

    public static bool TryNormalizeLocalReturnUrl(string? returnUrl, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(returnUrl))
            return false;

        var candidate = returnUrl.Trim();
        if (candidate.Any(char.IsControl) ||
            candidate.StartsWith("//", StringComparison.Ordinal) ||
            candidate.Contains('\\') ||
            candidate.Split(['/', '?', '#'], 2)[0].Contains(':'))
        {
            return false;
        }

        if (!candidate.StartsWith("/", StringComparison.Ordinal))
            candidate = "/" + candidate;

        if (!Uri.TryCreate(candidate, UriKind.Relative, out _))
            return false;

        var encodedPath = candidate.Split(['?', '#'], 2)[0];
        string decodedPath;
        try
        {
            decodedPath = Uri.UnescapeDataString(encodedPath);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (decodedPath.StartsWith("//", StringComparison.Ordinal) || decodedPath.Contains('\\'))
            return false;

        normalized = candidate;
        return true;
    }

    private static string? MasterDataRoutePermission(string path)
    {
        if (IsListOrGuidDetail(path, "master-data/countries"))
            return MasterDataPermissions.CountriesView;
        if (IsListOrGuidDetail(path, "master-data/manpower-types"))
            return MasterDataPermissions.ManpowerTypesView;
        if (IsListOrGuidDetail(path, "master-data/licenses"))
            return MasterDataPermissions.LicensesView;
        if (IsListOrGuidDetail(path, "master-data/services"))
            return MasterDataPermissions.ServicesView;
        if (IsListOrGuidDetail(path, "master-data/operation-types"))
            return MasterDataPermissions.OperationTypesView;
        if (IsListOrGuidDetail(path, "master-data/aircraft-types"))
            return MasterDataPermissions.AircraftTypesView;
        if (IsListOrGuidDetail(path, "master-data/tools"))
            return MasterDataPermissions.ToolsView;
        if (IsListOrGuidDetail(path, "master-data/materials"))
            return MasterDataPermissions.MaterialsView;
        if (IsListOrGuidDetail(path, "master-data/general-supports"))
            return MasterDataPermissions.GeneralSupportsView;
        if (IsListOrGuidDetail(path, "master-data/stations"))
            return MasterDataPermissions.StationsView;
        if (IsListOrGuidDetail(path, "master-data/customers"))
            return MasterDataPermissions.CustomersView;
        if (IsListOrGuidDetail(path, "master-data/staff-members"))
            return MasterDataPermissions.StaffMembersView;

        return null;
    }

    private static bool IsListOrGuidDetail(string path, string listPath) =>
        path == listPath || IsGuidDetail(path, listPath);

    private static bool IsGuidDetail(string path, string listPath)
    {
        if (!path.StartsWith(listPath + "/", StringComparison.Ordinal))
            return false;

        var id = path[(listPath.Length + 1)..];
        return !id.Contains('/') && Guid.TryParse(id, out _);
    }

    private static bool IsLoginRoute(string route) =>
        string.Equals(PathOnly(route), "login", StringComparison.OrdinalIgnoreCase);

    private static string PathOnly(string route) =>
        route.Split(['?', '#'], 2)[0].Trim('/');
}
