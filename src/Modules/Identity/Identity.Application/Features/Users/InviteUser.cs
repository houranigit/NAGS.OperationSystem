using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Application.Contracts;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Identity.Application.Features.Users;

/// <summary>
/// Direct user creation. v1.0.0 only creates System Administrators: the protected full-access role
/// is assigned automatically and there is no role selection. Station Staff and Customer Contact
/// accounts are provisioned from their MasterData record via the portal-access flow.
/// </summary>
public sealed record InviteUserCommand(string Email, string DisplayName) : ICommand<InvitedUserDto>;

public sealed class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
{
    public InviteUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(150);
    }
}

public sealed class InviteUserCommandHandler(
    IIdentityDbContext db,
    IInvitationNotifier invitationNotifier,
    ITokenService tokenService,
    TimeProvider timeProvider,
    IOptions<IdentityModuleOptions> options)
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

        // Direct creation always assigns the protected full-access System Administrator role.
        // StationStaff/CustomerContact accounts are provisioned from MasterData via portal access.
        var role = await db.Roles.FirstOrDefaultAsync(r => r.IsSystem, cancellationToken);
        if (role is null)
            return Error.Failure("The protected System Administrator role is not available.", "Identity.User.NoAdminRole");

        var now = timeProvider.GetUtcNow();
        var token = tokenService.CreateSecureToken();
        var expiry = now.AddHours(_options.InvitationExpiryHours);

        var userResult = User.Invite(email, request.DisplayName, role.Id, token.Hash, expiry, now, UserType.SystemAdministrator);
        if (userResult.IsFailure)
            return userResult.Error;

        db.Users.Add(userResult.Value);
        await db.SaveChangesAsync(cancellationToken);

        await invitationNotifier.SendInvitationAsync(email.Value, request.DisplayName, userResult.Value.Id, token.Value, cancellationToken);

        return new InvitedUserDto(userResult.Value.Id, email.Value, "Queued");
    }
}
