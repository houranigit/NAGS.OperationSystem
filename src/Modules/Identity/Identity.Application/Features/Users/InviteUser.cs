using BuildingBlocks.Application.Messaging;
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

        var emailTaken = await db.Users.AnyAsync(u => u.Email.Value == emailValue, cancellationToken);
        if (emailTaken)
            return Error.Conflict("A user with this email already exists.", "Identity.User.DuplicateEmail");

        var roleExists = await db.Roles.AnyAsync(r => r.Id == request.RoleId, cancellationToken);
        if (!roleExists)
            return Error.Validation("The selected role does not exist.", "Identity.User.RoleNotFound");

        var now = timeProvider.GetUtcNow();
        var token = Guid.NewGuid();
        var expiry = now.AddHours(_options.InvitationExpiryHours);

        var userResult = User.Invite(email, request.DisplayName, request.RoleId, token, expiry, now);
        if (userResult.IsFailure)
            return userResult.Error;

        db.Users.Add(userResult.Value);
        await db.SaveChangesAsync(cancellationToken);

        await invitationNotifier.SendInvitationAsync(email.Value, request.DisplayName, userResult.Value.Id, token, cancellationToken);

        return new InvitedUserDto(userResult.Value.Id, email.Value, token);
    }
}
