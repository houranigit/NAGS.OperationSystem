using FluentValidation;

namespace Identity.Application.Commands.UpdateUser;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email format is invalid.")
            .MaximumLength(254).WithMessage("Email must not exceed 254 characters.");

        RuleForEach(x => x.RoleIds)
            .NotEqual(Guid.Empty).WithMessage("Role id must not be empty.");
    }
}
