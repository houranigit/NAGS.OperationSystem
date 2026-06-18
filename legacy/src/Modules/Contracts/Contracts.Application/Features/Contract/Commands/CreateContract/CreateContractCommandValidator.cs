using Contracts.Application.Features.Contract.Shared;
using FluentValidation;

namespace Contracts.Application.Features.Contract.Commands.CreateContract;

/// <summary>
/// Surface-level shape checks. Domain invariants (waiver gaps, AOG-alone-per-OT, overlap)
/// live in <see cref="Contracts.Domain.Aggregates.Contract.Contract.Create"/> and are not
/// repeated here.
/// </summary>
public sealed class CreateContractCommandValidator : AbstractValidator<CreateContractCommand>
{
    public CreateContractCommandValidator()
    {
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
            .When(x => x.Period is not null)
            .WithMessage("Expiry alert days must be between 0 and 365.");

        RuleFor(x => x.FeesAndRates).NotNull().WithMessage("Fees and rates are required.");
        RuleFor(x => x.Cancellation).NotNull().WithMessage("Cancellation plan is required.");
        RuleFor(x => x.Delay).NotNull().WithMessage("Delay plan is required.");

        RuleFor(x => x.StationIds)
            .NotNull().WithMessage("Stations are required.")
            .Must(s => s is not null && s.Count >= 1)
            .WithMessage("At least one station is required.");

        RuleFor(x => x.OperationTypes)
            .NotNull().WithMessage("Operation types are required.")
            .Must(o => o is not null && o.Count >= 1)
            .WithMessage("At least one operation type is required.");

        // Each OT row needs at least one service id; domain will additionally enforce
        // AOG-alone-or-others-only based on the resolved service snapshots.
        RuleForEach(x => x.OperationTypes).ChildRules(ot =>
        {
            ot.RuleFor(o => o.OperationTypeId).NotEmpty()
                .WithMessage("Operation type id is required.");
            ot.RuleFor(o => o.ServiceIds)
                .NotNull().Must(ids => ids is not null && ids.Count >= 1)
                .WithMessage("Each operation type must have at least 1 service.");
        });

        // OT ids must be unique on the wizard payload.
        RuleFor(x => x.OperationTypes)
            .Must(list => list is null
                || list.Where(a => a is not null).Select(a => a.OperationTypeId).Distinct().Count()
                   == list.Count(a => a is not null))
            .WithMessage("Operation types must be unique.");

        // Service pricing rows are entirely optional now (an empty list means
        // "use system default pricing"). When populated, the (OT, Service, AircraftType)
        // tuple must be unique. The AOG-vs-others rule lives on the OT step now.
        RuleFor(x => x.Services).NotNull().WithMessage("Services list is required.");
        RuleFor(x => x.Services)
            .Must(ContractCommandValidationRules.HaveUniqueServiceScope)
            .When(x => x.Services is not null && x.Services.Count > 0)
            .WithMessage("A service with the same operation type and aircraft type is listed more than once.");

        RuleForEach(x => x.Services).ChildRules(svc =>
        {
            svc.RuleFor(s => s.OperationTypeId).NotEmpty().WithMessage("Service requires an operation type.");
            svc.RuleFor(s => s.ServiceId).NotEmpty().WithMessage("Service requires a service id.");
            svc.RuleFor(s => s.Brackets).NotNull().Must(b => b is not null && b.Count >= 1)
                .WithMessage("Each priced service requires at least 1 bracket.");
        });

        RuleForEach(x => x.Manpowers).ChildRules(mp =>
        {
            mp.RuleFor(m => m.OperationTypeId).NotEmpty().WithMessage("Manpower requires an operation type.");
            mp.RuleFor(m => m.ManpowerTypeId).NotEmpty().WithMessage("Manpower requires a manpower type.");
            mp.RuleFor(m => m.Brackets).NotNull().Must(b => b is not null && b.Count >= 1)
                .WithMessage("Each manpower requires at least 1 bracket.");
        });

        RuleForEach(x => x.Tools).ChildRules(tl =>
        {
            tl.RuleFor(t => t.OperationTypeId).NotEmpty().WithMessage("Tool requires an operation type.");
            tl.RuleFor(t => t.ToolId).NotEmpty().WithMessage("Tool requires a tool id.");
            tl.RuleFor(t => t.Brackets).NotNull().Must(b => b is not null && b.Count >= 1)
                .WithMessage("Each tool requires at least 1 bracket.");
        });

        RuleForEach(x => x.Materials).ChildRules(mat =>
        {
            mat.RuleFor(m => m.OperationTypeId).NotEmpty().WithMessage("Material requires an operation type.");
            mat.RuleFor(m => m.MaterialId).NotEmpty().WithMessage("Material requires a material id.");
            mat.RuleFor(m => m.Brackets).NotNull().Must(b => b is not null && b.Count >= 1)
                .WithMessage("Each material requires at least 1 bracket.");
        });

        RuleForEach(x => x.GeneralSupports).ChildRules(gs =>
        {
            gs.RuleFor(g => g.OperationTypeId).NotEmpty().WithMessage("General-support requires an operation type.");
            gs.RuleFor(g => g.GeneralSupportId).NotEmpty().WithMessage("General-support requires a general-support id.");
            gs.RuleFor(g => g.Brackets).NotNull().Must(b => b is not null && b.Count >= 1)
                .WithMessage("Each general-support requires at least 1 bracket.");
        });

        // Per-OT advance payments — empty is fine (means "no advance payments configured");
        // when populated, every row needs valid amounts and the OT must appear at most once
        // across the list. Domain reapplies these checks plus "OT exists on contract".
        RuleFor(x => x.AdvancePayments).NotNull().WithMessage("Advance payments list is required.");
        RuleFor(x => x.AdvancePayments)
            .Must(list => list is null
                || list.Where(a => a is not null).Select(a => a.OperationTypeId).Distinct().Count()
                   == list.Count(a => a is not null))
            .WithMessage("Advance payments must be unique per operation type.");

        RuleForEach(x => x.AdvancePayments).ChildRules(ap =>
        {
            ap.RuleFor(a => a.OperationTypeId).NotEmpty().WithMessage("Advance payment requires an operation type.");
            ap.RuleFor(a => a.FlightsCount).GreaterThan(0).WithMessage("Advance payment flights count must be greater than zero.");
            ap.RuleFor(a => a.FlightCost).GreaterThan(0m).WithMessage("Advance payment flight cost must be greater than zero.");
            ap.RuleFor(a => a.Balance).GreaterThan(0m).WithMessage("Advance payment balance must be greater than zero.");
            ap.RuleFor(a => a.Deposit).GreaterThanOrEqualTo(0m).WithMessage("Advance payment deposit cannot be negative.");
        });
    }
}
