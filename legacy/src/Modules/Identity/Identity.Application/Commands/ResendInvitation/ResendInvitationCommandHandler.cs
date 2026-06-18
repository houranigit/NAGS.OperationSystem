using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Application.EmailTemplates;
using Identity.Domain.Aggregates.User;

namespace Identity.Application.Commands.ResendInvitation;

/// <summary>
/// Re-issues a fresh invitation token through <see cref="User.RegenerateInvitationToken"/> and ships
/// a new activation email. Note: SaveChanges is owned by <c>TransactionBehavior</c>; the email is
/// dispatched after the persist call below so a failed save short-circuits before we email the user
/// — but the email itself is fire-and-forget by design (SmtpEmailSender swallows failures).
/// </summary>
public sealed class ResendInvitationCommandHandler(
    IUserRepository userRepository,
    IInvitationTokenGenerator tokenGenerator,
    IEmailSender emailSender,
    IInvitationEmailComposer emailComposer)
    : ICommandHandler<ResendInvitationCommand>
{
    public async Task<Result> Handle(ResendInvitationCommand request, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(UserId.From(request.UserId), cancellationToken);
        if (user is null) return Error.NotFound("User was not found.");

        var newToken = tokenGenerator.Generate();
        var expiresAt = DateTime.UtcNow.Add(tokenGenerator.ExpiryDuration);

        var regen = user.RegenerateInvitationToken(newToken, expiresAt);
        if (regen.IsFailure) return regen.Error;

        userRepository.Update(user);

        var email = emailComposer.BuildInvitation(
            recipientEmail: user.Email.Value,
            recipientDisplayName: user.Username.Value,
            invitationToken: newToken,
            expiresAtUtc: expiresAt);

        await emailSender.SendAsync(email, cancellationToken);
        return Result.Success();
    }
}
