using BuildingBlocks.Application.Abstractions.Commands;

namespace Identity.Application.Commands.ResendInvitation;

/// <summary>
/// Re-issues a fresh invitation token for a <c>PendingActivation</c> user and sends a new
/// activation email. Useful when the original invite expired or never reached the recipient.
/// </summary>
public sealed record ResendInvitationCommand(Guid UserId) : ICommand;
