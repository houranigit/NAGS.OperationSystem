using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Users;

public enum UserAccessChangeKind
{
    Unsupported,
    RoleOnly,
    ElevationToAdministrator,
    DemotionToViewer
}

public enum UserAccessRecoveryAction
{
    None,
    ReloadRoles,
    CloseAndReloadUser
}

/// <summary>
/// Pure client-side presentation policy for the change-access workflow. The API remains
/// authoritative for authorization, compatibility, concurrency, and last-administrator guards.
/// </summary>
public static class UserAccessChangePolicy
{
    public static bool IsDirectType(string? userType) =>
        string.Equals(userType, UserTypes.SystemAdministrator, StringComparison.Ordinal)
        || string.Equals(userType, UserTypes.ViewerOnly, StringComparison.Ordinal);

    public static IReadOnlyList<string> AllowedTargetTypes(
        string currentUserType,
        bool canChangeAccountType)
    {
        if (canChangeAccountType && IsDirectType(currentUserType))
            return UserTypes.Direct;

        return string.IsNullOrWhiteSpace(currentUserType) ? [] : [currentUserType];
    }

    public static bool IsTargetTypeAllowed(
        string currentUserType,
        string targetUserType,
        bool canChangeAccountType) =>
        AllowedTargetTypes(currentUserType, canChangeAccountType)
            .Contains(targetUserType, StringComparer.Ordinal);

    public static bool CanShowAction(
        Guid? actingUserId,
        Guid targetUserId,
        string targetStatus) =>
        actingUserId is { } actorId
        && actorId != targetUserId
        && !string.Equals(targetStatus, "Deactivated", StringComparison.Ordinal);

    public static bool IsNoOp(
        string currentUserType,
        Guid currentRoleId,
        string targetUserType,
        Guid? targetRoleId) =>
        targetRoleId == currentRoleId
        && string.Equals(currentUserType, targetUserType, StringComparison.Ordinal);

    public static bool RequiresConfirmation(
        string currentUserType,
        Guid currentRoleId,
        string targetUserType,
        Guid? targetRoleId,
        bool canChangeAccountType) =>
        targetRoleId is not null
        && IsTargetTypeAllowed(currentUserType, targetUserType, canChangeAccountType)
        && Classify(currentUserType, targetUserType) is not UserAccessChangeKind.Unsupported
        && !IsNoOp(currentUserType, currentRoleId, targetUserType, targetRoleId);

    public static bool ShouldAcceptRoleLoadResult(
        int requestGeneration,
        int currentGeneration,
        bool isCanceled) =>
        !isCanceled && requestGeneration == currentGeneration;

    public static bool IsStaleProblemCode(string? problemCode) =>
        problemCode is "General.ConcurrencyConflict" or "General.PreconditionRequired";

    public static bool CanStartConfirmation(bool isConfirming, bool isSubmitting) =>
        !isConfirming && !isSubmitting;

    public static UserAccessRecoveryAction RecoveryForProblemCode(string? problemCode) =>
        problemCode switch
        {
            "General.ConcurrencyConflict" or
            "General.PreconditionRequired" or
            "Identity.User.CurrentRoleNotFound" or
            "Identity.User.CurrentRoleIncompatible" or
            "Identity.User.CurrentRoleInvalid" =>
                UserAccessRecoveryAction.CloseAndReloadUser,

            "Identity.User.RoleNotFound" or
            "Identity.User.SelectedRoleInvalid" or
            "Identity.Role.UnknownPermission" or
            "Identity.Role.IncompatiblePermission" or
            "Identity.Role.ViewerPagePermissionRequired" =>
                UserAccessRecoveryAction.ReloadRoles,

            _ => UserAccessRecoveryAction.None
        };

    public static UserAccessChangeKind Classify(
        string currentUserType,
        string targetUserType)
    {
        if (string.Equals(currentUserType, targetUserType, StringComparison.Ordinal))
            return UserAccessChangeKind.RoleOnly;

        if (string.Equals(currentUserType, UserTypes.ViewerOnly, StringComparison.Ordinal)
            && string.Equals(targetUserType, UserTypes.SystemAdministrator, StringComparison.Ordinal))
        {
            return UserAccessChangeKind.ElevationToAdministrator;
        }

        if (string.Equals(currentUserType, UserTypes.SystemAdministrator, StringComparison.Ordinal)
            && string.Equals(targetUserType, UserTypes.ViewerOnly, StringComparison.Ordinal))
        {
            return UserAccessChangeKind.DemotionToViewer;
        }

        return UserAccessChangeKind.Unsupported;
    }
}
