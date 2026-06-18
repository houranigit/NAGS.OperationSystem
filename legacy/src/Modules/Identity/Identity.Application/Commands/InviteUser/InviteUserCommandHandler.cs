using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Enumerations;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Application.EmailTemplates;
using Identity.Domain.Aggregates.Role;
using Identity.Domain.Aggregates.User;
using Identity.Domain.Enumerations;
using Identity.Domain.ValueObjects;

namespace Identity.Application.Commands.InviteUser;

/// <summary>
/// Creates an invited (PendingActivation) user, optionally seeds roles, and dispatches an
/// activation email. Email send is fire-and-forget by design (SmtpEmailSender swallows
/// failures) — the user always lands in the database, the admin can resend the invite later.
/// </summary>
public sealed class InviteUserCommandHandler(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IInvitationTokenGenerator tokenGenerator,
    IEmailSender emailSender,
    IInvitationEmailComposer emailComposer)
    : ICommandHandler<InviteUserCommand, InviteUserResult>
{
    public async Task<Result<InviteUserResult>> Handle(
        InviteUserCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await userRepository.GetByEmailAsync(command.Email, cancellationToken);
        if (existing is not null)
            return Error.Conflict($"A user with email '{command.Email}' already exists.");

        var usernameResult = Username.Create(command.Username);
        if (!usernameResult.IsSuccess) return usernameResult.Error;

        var emailResult = Email.Create(command.Email);
        if (!emailResult.IsSuccess) return emailResult.Error;

        var userType = Enumeration.FromValue<UserType>(command.UserTypeId);
        if (userType is null)
            return Error.Validation($"Invalid UserTypeId: {command.UserTypeId}.");

        var token = tokenGenerator.Generate();
        var tokenExpiry = DateTime.UtcNow.Add(tokenGenerator.ExpiryDuration);

        var userResult = User.Invite(
            usernameResult.Value,
            emailResult.Value,
            userType,
            token,
            tokenExpiry,
            command.ExternalReferenceId);

        if (!userResult.IsSuccess) return userResult.Error;

        var user = userResult.Value;

        if (command.RoleIds is { Count: > 0 })
        {
            var desiredRoleIds = command.RoleIds.Select(RoleId.From).ToHashSet();
            var existingRoles = await roleRepository.GetByIdsAsync(desiredRoleIds, cancellationToken);
            var existingRoleIds = existingRoles.Select(r => r.Id).ToHashSet();
            var missingRoleIds = desiredRoleIds.Except(existingRoleIds).ToList();
            if (missingRoleIds.Count > 0)
                return Error.Validation("One or more selected roles no longer exist. Refresh the page and try again.");

            foreach (var roleId in desiredRoleIds)
            {
                var assign = user.AssignRole(roleId);
                if (assign.IsFailure) return assign.Error;
            }
        }

        userRepository.Add(user);

        var invitationEmail = emailComposer.BuildInvitation(
            recipientEmail: user.Email.Value,
            recipientDisplayName: user.Username.Value,
            invitationToken: token,
            expiresAtUtc: tokenExpiry);

        await emailSender.SendAsync(invitationEmail, cancellationToken);

        return new InviteUserResult(user.Id.Value, token);
    }
}
