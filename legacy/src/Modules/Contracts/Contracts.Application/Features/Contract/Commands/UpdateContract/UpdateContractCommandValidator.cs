using Contracts.Application.Features.Contract.Shared;
using FluentValidation;

namespace Contracts.Application.Features.Contract.Commands.UpdateContract;

public sealed class UpdateContractCommandValidator : AbstractValidator<UpdateContractCommand>
{
    public UpdateContractCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Contract id is required.");

        RuleFor(x => x.ContractNo)
            .NotEmpty().WithMessage("Contract number is required.")
            .MinimumLength(3).WithMessage("Contract number must be at least 3 characters.")
            .MaximumLength(30).WithMessage("Contract number must not exceed 30 characters.");

        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("Customer is required.");
        RuleFor(x => x.CurrencyId).NotEmpty().WithMessage("Currency is required.");

        RuleFor(x => x.Period).NotNull().WithMessage("Contract period is required.");
        RuleFor(x => x.Period.StartDate).LessThan(x => x.Period.ExpiryDate)
            .When(x => x.Period is not null)
            .WithMessage("Start date must be before expiry date.");
        RuleFor(x => x.Period.ExpiryAlertDays)
            .InclusiveBetween(0, 365)
            .When(x => x.Period is not null);

        RuleFor(x => x.FeesAndRates).NotNull();
        RuleFor(x => x.Cancellation).NotNull();
        RuleFor(x => x.Delay).NotNull();

        RuleFor(x => x.StationIds).NotNull().Must(s => s is not null && s.Count >= 1)
            .WithMessage("At least one station is required.");
        RuleFor(x => x.OperationTypes).NotNull().Must(o => o is not null && o.Count >= 1)
            .WithMessage("At least one operation type is required.");

        RuleForEach(x => x.OperationTypes).ChildRules(ot =>
        {
            ot.RuleFor(o => o.OperationTypeId).NotEmpty()
                .WithMessage("Operation type id is required.");
            ot.RuleFor(o => o.ServiceIds)
                .NotNull().Must(ids => ids is not null && ids.Count >= 1)
                .WithMessage("Each operation type must have at least 1 service.");
        });

        RuleFor(x => x.OperationTypes)
            .Must(list => list is null
                || list.Where(a => a is not null).Select(a => a.OperationTypeId).Distinct().Count()
                   == list.Count(a => a is not null))
            .WithMessage("Operation types must be unique.");

        // Service pricing rows are entirely optional now (an empty list means "use system
        // default pricing"). When populated, the (OT, Service, AircraftType) tuple must be
        // unique. The AOG-vs-others rule lives on the OT step now.
        RuleFor(x => x.Services).NotNull().WithMessage("Services list is required.");
        RuleFor(x => x.Services)
            .Must(ContractCommandValidationRules.HaveUniqueServiceScope)
            .When(x => x.Services is not null && x.Services.Count > 0)
            .WithMessage("A service with the same operation type and aircraft type is listed more than once.");

        RuleForEach(x => x.Services).ChildRules(svc =>
        {
            svc.RuleFor(s => s.OperationTypeId).NotEmpty();
            svc.RuleFor(s => s.ServiceId).NotEmpty();
            svc.RuleFor(s => s.Brackets).NotNull().Must(b => b is not null && b.Count >= 1)
                .WithMessage("Each priced service requires at least 1 bracket.");
        });

        RuleForEach(x => x.Manpowers).ChildRules(mp =>
        {
            mp.RuleFor(m => m.OperationTypeId).NotEmpty();
            mp.RuleFor(m => m.ManpowerTypeId).NotEmpty();
            mp.RuleFor(m => m.Brackets).NotNull().Must(b => b is not null && b.Count >= 1)
                .WithMessage("Each manpower requires at least 1 bracket.");
        });

        RuleForEach(x => x.Tools).ChildRules(tl =>
        {
            tl.RuleFor(t => t.OperationTypeId).NotEmpty();
            tl.RuleFor(t => t.ToolId).NotEmpty();
            tl.RuleFor(t => t.Brackets).NotNull().Must(b => b is not null && b.Count >= 1)
                .WithMessage("Each tool requires at least 1 bracket.");
        });

        RuleForEach(x => x.Materials).ChildRules(mat =>
        {
            mat.RuleFor(m => m.OperationTypeId).NotEmpty();
            mat.RuleFor(m => m.MaterialId).NotEmpty();
            mat.RuleFor(m => m.Brackets).NotNull().Must(b => b is not null && b.Count >= 1)
                .WithMessage("Each material requires at least 1 bracket.");
        });

        RuleForEach(x => x.GeneralSupports).ChildRules(gs =>
        {
            gs.RuleFor(g => g.OperationTypeId).NotEmpty();
            gs.RuleFor(g => g.GeneralSupportId).NotEmpty();
            gs.RuleFor(g => g.Brackets).NotNull().Must(b => b is not null && b.Count >= 1)
                .WithMessage("Each general-support requires at least 1 bracket.");
        });

        // Per-OT advance payments — see CreateContractCommandValidator for the same shape.
        RuleFor(x => x.AdvancePayments).NotNull().WithMessage("Advance payments list is required.");
        RuleFor(x => x.AdvancePayments)
            .Must(list => list is null
                || list.Where(a => a is not null).Select(a => a.OperationTypeId).Distinct().Count()
                   == list.Count(a => a is not null))
            .WithMessage("Advance payments must be unique per operation type.");

        RuleForEach(x => x.AdvancePayments).ChildRules(ap =>
        {
            ap.RuleFor(a => a.OperationTypeId).NotEmpty().WithMessage("Advance payment requires an operation type.");
            ap.RuleFor(a => a.FlightsCount).GreaterThan(0);
            ap.RuleFor(a => a.FlightCost).GreaterThan(0m);
            ap.RuleFor(a => a.Balance).GreaterThan(0m);
            ap.RuleFor(a => a.Deposit).GreaterThanOrEqualTo(0m);
        });
    }
}
