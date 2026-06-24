using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Auth;

/// <summary>
/// Finalizes a linked email change once the recipient verifies the new address. The login email only
/// changes here, after verification, so an undeliverable address never locks the account out.
/// </summary>
public sealed record ConfirmEmailChangeCommand(string Token, string NewEmail) : ICommand;

public sealed class ConfirmEmailChangeCommandValidator : AbstractValidator<ConfirmEmailChangeCommand>
{
    public ConfirmEmailChangeCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewEmail).NotEmpty();
    }
}

public sealed class ConfirmEmailChangeCommandHandler(IIdentityDbContext db, ITokenService tokenService, TimeProvider timeProvider)
    : ICommandHandler<ConfirmEmailChangeCommand>
{
    public async Task<Result> Handle(ConfirmEmailChangeCommand request, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.NewEmail);
        if (emailResult.IsFailure)
            return emailResult.Error;

        var now = timeProvider.GetUtcNow();
        var tokenHash = tokenService.HashToken(request.Token);
        var user = await db.Users.FirstOrDefaultAsync(u => u.EmailChangeToken == tokenHash, cancellationToken);
        if (user is null)
            return Error.NotFound("There is no pending email change for this token.", "Identity.User.NoPendingEmailChange");

        if (user.EmailChangeExpiresAtUtc is { } expiry && expiry <= now)
            return Error.Conflict("The email-change verification has expired.", "Identity.User.EmailChangeExpired");

        var emailValue = emailResult.Value.Value;
        var taken = await db.Users.AnyAsync(u => u.Email.Value == emailValue && !u.LoginEmailReleased && u.Id != user.Id, cancellationToken);
        if (taken)
            return Error.Conflict("A user with this email already exists.", "Identity.User.DuplicateEmail");

        var result = user.ConfirmEmailChange(emailResult.Value, now);
        if (result.IsFailure)
            return result.Error;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
