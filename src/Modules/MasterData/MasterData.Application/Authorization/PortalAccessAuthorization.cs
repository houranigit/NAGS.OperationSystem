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

    /// <summary>
    /// Captures the authenticated Identity user that initiated a portal-role delegation. The
    /// consumer uses this stable id to re-evaluate the initiator's live role and permission ceiling.
    /// </summary>
    public static Result<Guid> ResolveInitiatingUserId(IUserContext userContext) =>
        userContext.IsAuthenticated && userContext.UserId is { } userId && userId != Guid.Empty
            ? userId
            : Error.Unauthorized(
                "An authenticated Identity user is required to grant portal access.",
                "MasterData.PortalAccess.InitiatorRequired");

    public static Error ReleaseEmailForbidden() =>
        Error.Forbidden(
            "Releasing a portal login email requires administrator grant-access permission.",
            "MasterData.PortalAccess.ReleaseEmailForbidden");

    private static bool IsAdministrator(IUserContext userContext) =>
        userContext.UserType == UserType.SystemAdministrator;
}
