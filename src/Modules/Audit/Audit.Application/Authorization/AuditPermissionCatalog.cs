using Audit.Domain.Authorization;
using BuildingBlocks.Contracts.Authorization;

namespace Audit.Application.Authorization;

/// <summary>Contributes the Audit module's permissions. Reading the trail is administrator-only.</summary>
public sealed class AuditPermissionCatalog : IPermissionCatalog
{
    private static readonly IReadOnlyList<UserType> AdminOnly = [UserType.SystemAdministrator];

    public string Module => "audit";

    public IReadOnlyList<PermissionDescriptor> Permissions { get; } =
        AuditPermissions.All.Select(p => new PermissionDescriptor(p, AdminOnly)).ToList();
}
