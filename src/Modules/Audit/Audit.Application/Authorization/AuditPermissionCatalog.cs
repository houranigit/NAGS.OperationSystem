using Audit.Domain.Authorization;
using BuildingBlocks.Contracts.Authorization;

namespace Audit.Application.Authorization;

/// <summary>Contributes the Audit module's read-only trail page.</summary>
public sealed class AuditPermissionCatalog : IPermissionCatalog
{
    private static readonly IReadOnlyList<UserType> AdminAndViewer =
        [UserType.SystemAdministrator, UserType.ViewerOnly];

    public string Module => "audit";

    public IReadOnlyList<PermissionDescriptor> Permissions { get; } =
    [
        new(AuditPermissions.Trails.View, AdminAndViewer, GrantsPortalPage: true)
    ];
}
