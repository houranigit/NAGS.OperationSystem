using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using Identity.Domain.Authorization;

namespace Identity.Application.Commands.UpdateUser;

/// <summary>
/// Updates the email of an existing user (username is immutable in this build), plus replaces the
/// assigned-role set in a single operation — mirrors the "save the dialog" semantics on the front-end.
/// Use <c>RemoveRole</c> / <c>AssignRole</c> for fine-grained per-row changes from the Roles page.
/// </summary>
public sealed record UpdateUserCommand(
    Guid Id,
    string Email,
    bool IsActive,
    IReadOnlyList<Guid> RoleIds) : ICommand, IRequirePermission
{
    public string RequiredPermission => Permissions.Users.AssignRoles;
}
