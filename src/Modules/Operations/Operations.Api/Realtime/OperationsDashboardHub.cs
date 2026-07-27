using BuildingBlocks.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Operations.Domain.Authorization;

namespace Operations.Api.Realtime;

/// <summary>
/// Authenticated invalidation channel for the operations analytics dashboard.
/// No dashboard data is pushed over the hub; clients re-query the permission- and scope-protected
/// REST endpoints after receiving <see cref="DashboardChangedClientMethod"/>.
/// </summary>
[Authorize(Policy = PermissionPolicy.Prefix + OperationsPermissions.Dashboard.ViewAnalytics)]
public sealed class OperationsDashboardHub : Hub
{
    public const string Path = "/hubs/operations-dashboard";
    public const string DashboardChangedClientMethod = "dashboardChanged";
}
