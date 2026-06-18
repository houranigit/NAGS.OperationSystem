using BuildingBlocks.Application.Abstractions.Commands;

namespace Identity.Application.Commands.RevokeSession;

/// <summary>
/// No IRequirePermission — handler checks that the caller is the session owner
/// or has Sessions.Revoke permission.
/// </summary>
public sealed record RevokeSessionCommand(Guid SessionId, Guid UserId) : ICommand;
