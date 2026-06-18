using FluentValidation;

namespace Contracts.Application.Features.Contract.Commands.ActivateContract;

public sealed class ActivateContractCommandValidator : AbstractValidator<ActivateContractCommand>
{
    public ActivateContractCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Contract id is required.");
    }
}
