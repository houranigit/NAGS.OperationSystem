using FluentValidation;

namespace Contracts.Application.Features.Contract.Commands.TerminateContract;

public sealed class TerminateContractCommandValidator : AbstractValidator<TerminateContractCommand>
{
    public TerminateContractCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Contract id is required.");
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Termination reason is required.")
            .MaximumLength(500);
    }
}
