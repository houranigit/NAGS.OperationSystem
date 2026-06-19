using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Auth;

public sealed record ActivateAccountCommand(string Email, Guid InvitationToken, string NewPassword) : ICommand;

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
    TimeProvider timeProvider)
    : ICommandHandler<ActivateAccountCommand>
{
    public async Task<Result> Handle(ActivateAccountCommand request, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
            return emailResult.Error;

        var emailValue = emailResult.Value.Value;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.Value == emailValue, cancellationToken);
        if (user is null)
            return Error.NotFound("Account not found.", "Identity.User.NotFound");

        var hash = passwordHasher.Hash(request.NewPassword);
        var result = user.Activate(request.InvitationToken, hash, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
