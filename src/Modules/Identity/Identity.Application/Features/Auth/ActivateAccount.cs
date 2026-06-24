using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Auth;

public sealed record ActivateAccountCommand(string Email, string InvitationToken, string NewPassword) : ICommand;

public sealed class ActivateAccountCommandValidator : AbstractValidator<ActivateAccountCommand>
{
    public ActivateAccountCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.InvitationToken).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}

public sealed class ActivateAccountCommandHandler(
    IIdentityDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider)
    : ICommandHandler<ActivateAccountCommand>
{
    public async Task<Result> Handle(ActivateAccountCommand request, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
            return emailResult.Error;

        // Non-enumerating: an unknown email, wrong token, or expired invitation all return the same
        // generic result so the endpoint cannot be used to discover which emails have accounts.
        var invalidInvitation = Error.Validation(
            "The invitation link is invalid or has expired.", "Identity.Auth.InvalidInvitation");

        var emailValue = emailResult.Value.Value;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.Value == emailValue, cancellationToken);
        if (user is null)
            return invalidInvitation;

        var hash = passwordHasher.Hash(request.NewPassword);
        var tokenHash = tokenService.HashToken(request.InvitationToken);
        var result = user.Activate(tokenHash, hash, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return invalidInvitation;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
