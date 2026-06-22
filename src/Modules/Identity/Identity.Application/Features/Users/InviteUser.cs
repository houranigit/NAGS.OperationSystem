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

public sealed record InviteUserCommand(string Email, string DisplayName, Guid RoleId) : ICommand<InvitedUserDto>;

public sealed class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
{
    public InviteUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class InviteUserCommandHandler(
    IIdentityDbContext db,
    IInvitationNotifier invitationNotifier,
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

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);
        if (role is null)
            return Error.Validation("The selected role does not exist.", "Identity.User.RoleNotFound");

        // Identity only directly invites administrators. StationStaff/CustomerContact accounts are
        // provisioned from their MasterData record via the portal-access integration flow.
        if (role.CompatibleUserType != UserType.SystemAdministrator)
            return Error.Conflict(
                "Only System Administrator accounts can be invited directly. Grant portal access from the staff member or customer contact instead.",
                "Identity.User.PortalAccountFromMasterData");

        var now = timeProvider.GetUtcNow();
        var token = Guid.NewGuid();
        var expiry = now.AddHours(_options.InvitationExpiryHours);

        var userResult = User.Invite(email, request.DisplayName, request.RoleId, token, expiry, now, UserType.SystemAdministrator);
        if (userResult.IsFailure)
            return userResult.Error;

        db.Users.Add(userResult.Value);
        await db.SaveChangesAsync(cancellationToken);

        await invitationNotifier.SendInvitationAsync(email.Value, request.DisplayName, userResult.Value.Id, token, cancellationToken);

        return new InvitedUserDto(userResult.Value.Id, email.Value, token);
    }
}
