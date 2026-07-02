using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Domain.Authorization;

namespace MasterData.Application.Authorization;

internal static class PortalAccessAuthorization
{
    public static bool CanGrantStaffAccess(IUserContext userContext) =>
        IsAdministrator(userContext)
        && userContext.HasPermission(MasterDataPermissions.StaffMembers.GrantAccess);

    public static bool CanGrantCustomerContactAccess(IUserContext userContext) =>
        IsAdministrator(userContext)
        && userContext.HasPermission(MasterDataPermissions.CustomerContacts.GrantAccess);

    public static Error GrantForbidden() =>
        Error.Forbidden(
            "Granting portal access requires administrator grant-access permission.",
            "MasterData.PortalAccess.Forbidden");

    public static Error ReleaseEmailForbidden() =>
        Error.Forbidden(
            "Releasing a portal login email requires administrator grant-access permission.",
            "MasterData.PortalAccess.ReleaseEmailForbidden");

    private static bool IsAdministrator(IUserContext userContext) =>
        userContext.UserType == UserType.SystemAdministrator;
}
