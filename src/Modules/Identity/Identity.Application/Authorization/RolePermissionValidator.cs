using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;

namespace Identity.Application.Authorization;

/// <summary>
/// Validates that a set of permissions is known to the composed catalog and compatible with a
/// role's user type. A role may never contain a permission outside its user type's maximum set.
/// </summary>
public static class RolePermissionValidator
{
    public static Result Validate(IReadOnlyList<string> requested, UserType userType, IPermissionRegistry registry)
    {
        foreach (var permission in requested.Distinct())
        {
            if (!registry.IsKnown(permission))
                return Error.Validation($"Unknown permission '{permission}'.", "Identity.Role.UnknownPermission");

            if (!registry.IsCompatibleWith(permission, userType))
                return Error.Validation(
                    $"Permission '{permission}' is not compatible with {userType} roles.",
                    "Identity.Role.IncompatiblePermission");
        }

        if (userType == UserType.ViewerOnly)
        {
            var requestedCodes = requested.ToHashSet(StringComparer.Ordinal);
            var grantsPortalPage = registry.All.Any(
                descriptor =>
                    descriptor.GrantsPortalPage &&
                    descriptor.IsCompatibleWith(userType) &&
                    requestedCodes.Contains(descriptor.Code));

            if (!grantsPortalPage)
            {
                return Error.Validation(
                    "A Viewer Only role must grant access to at least one portal page.",
                    "Identity.Role.ViewerPagePermissionRequired");
            }
        }

        return Result.Success();
    }
}
