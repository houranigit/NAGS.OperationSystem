using BuildingBlocks.Application.Abstractions.Commands;

namespace Identity.Application.Commands.ChangePassword;

/// <summary>
/// No IRequirePermission — handler checks that caller is the user themselves
/// or has Users.Update permission.
/// </summary>
public sealed record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword
) : ICommand;
