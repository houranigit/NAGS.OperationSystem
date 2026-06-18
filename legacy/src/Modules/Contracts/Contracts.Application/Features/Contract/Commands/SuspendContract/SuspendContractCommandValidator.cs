using FluentValidation;

namespace Contracts.Application.Features.Contract.Commands.SuspendContract;

public sealed class SuspendContractCommandValidator : AbstractValidator<SuspendContractCommand>
{
    public SuspendContractCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Contract id is required.");
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Suspend reason is required.")
            .MaximumLength(500);
    }
}
