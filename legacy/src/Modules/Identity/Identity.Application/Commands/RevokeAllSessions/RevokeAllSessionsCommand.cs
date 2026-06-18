using BuildingBlocks.Application.Abstractions.Commands;

namespace Identity.Application.Commands.RevokeAllSessions;

/// <summary>
/// Revokes all active sessions for a user.
/// Handler checks that the caller is the user themselves or has Sessions.Revoke.
/// </summary>
public sealed record RevokeAllSessionsCommand(Guid UserId) : ICommand;
