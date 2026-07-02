using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Contracts;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Application.Features.Users;

/// <summary>
/// Direct user creation only creates System Administrators. Station Staff and Customer Contact
/// accounts are provisioned from their MasterData record via the portal-access flow.
/// </summary>
public sealed record InviteUserCommand(string Email, string DisplayName, Guid? RoleId = null) : ICommand<InvitedUserDto>;

public sealed class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
{
    public InviteUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RoleId).Must(id => id is null || id.Value != Guid.Empty)
            .WithMessage("A valid role is required.");
    }
}

public sealed class InviteUserCommandHandler(
    IIdentityDbContext db,
    IInvitationNotifier invitationNotifier,
    ITokenService tokenService,
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options,
    ILogger<InviteUserCommandHandler> logger)
    : ICommandHandler<InviteUserCommand, InvitedUserDto>
{
    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result<InvitedUserDto>> Handle(InviteUserCommand request, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
            return emailResult.Error;

        var email = emailResult.Value;
        var emailValue = email.Value;

        // Only an active login email participates in uniqueness; released emails are reusable.
        var emailTaken = await db.Users.AnyAsync(u => u.Email.Value == emailValue && !u.LoginEmailReleased, cancellationToken);
        if (emailTaken)
            return Error.Conflict("A user with this email already exists.", "Identity.User.DuplicateEmail");

        // Direct creation is administrator-only, but the inviter may choose any compatible
        // SystemAdministrator role so new accounts do not need to start with full access.
        var role = request.RoleId is { } roleId
            ? await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            : await db.Roles.FirstOrDefaultAsync(r => r.IsSystem, cancellationToken);
        if (role is null)
            return request.RoleId is { }
                ? Error.Validation("The selected role does not exist.", "Identity.User.RoleNotFound")
                : Error.Failure("The protected System Administrator role is not available.", "Identity.User.NoAdminRole");

        if (role.CompatibleUserType != UserType.SystemAdministrator)
        {
            return Error.Conflict(
                $"Role '{role.Name}' is not compatible with direct administrator invitations.",
                "Identity.User.IncompatibleRole");
        }

        var now = timeProvider.GetUtcNow();
        var token = tokenService.CreateSecureToken();
        var expiry = now.AddHours(_options.InvitationExpiryHours);

        var userResult = User.Invite(email, request.DisplayName, role.Id, token.Hash, expiry, now, UserType.SystemAdministrator);
        if (userResult.IsFailure)
            return userResult.Error;

        db.Users.Add(userResult.Value);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await invitationNotifier.SendInvitationAsync(email.Value, request.DisplayName, userResult.Value.Id, token.Value, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Invitation delivery failed for direct user {UserId}.", userResult.Value.Id);
            return new InvitedUserDto(userResult.Value.Id, email.Value, "Failed");
        }

        return new InvitedUserDto(userResult.Value.Id, email.Value, "Queued");
    }
}
