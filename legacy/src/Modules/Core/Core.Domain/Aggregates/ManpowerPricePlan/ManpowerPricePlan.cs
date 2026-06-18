using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.ManpowerType;
using Core.Domain.Aggregates.OperationType;
using Core.Domain.Events;
using Core.Domain.Enumerations;
using Core.Domain.Pricing;

namespace Core.Domain.Aggregates.ManpowerPricePlan;

/// <summary>
/// System-default price plan for a given (ManpowerType, OperationType) pair. Consumed as a
/// fallback by the Operations/Billing module when no contract-level override exists.
/// </summary>
public sealed class ManpowerPricePlan : AggregateRoot<ManpowerPricePlanId>
{
    private readonly List<PriceBracket> _brackets = [];

    public ManpowerTypeId ManpowerTypeId { get; private set; } = null!;
    public OperationTypeId OperationTypeId { get; private set; } = null!;
    public CurrencyId CurrencyId { get; private set; } = null!;
    public PricingBasis Basis { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyList<PriceBracket> Brackets => _brackets.AsReadOnly();

    private ManpowerPricePlan() { }

    public static Result<ManpowerPricePlan> Create(
        ManpowerTypeId manpowerTypeId,
        OperationTypeId operationTypeId,
        CurrencyId currencyId,
        PricingBasis basis,
        IEnumerable<PriceBracket> brackets)
    {
        ArgumentNullException.ThrowIfNull(manpowerTypeId);
        ArgumentNullException.ThrowIfNull(operationTypeId);
        ArgumentNullException.ThrowIfNull(currencyId);

        var rows = (brackets ?? []).ToList();
        var validation = PricePlanValidator.Validate(basis, rows);
        if (validation.IsFailure) return validation.Error;

        var plan = new ManpowerPricePlan
        {
            Id = ManpowerPricePlanId.New(),
            ManpowerTypeId = manpowerTypeId,
            OperationTypeId = operationTypeId,
            CurrencyId = currencyId,
            Basis = basis,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        plan._brackets.AddRange(rows);

        plan.RaiseDomainEvent(new ManpowerPricePlanCreatedEvent(plan.Id));
        return plan;
    }

    public Result UpdateBasics(CurrencyId currencyId, PricingBasis basis)
    {
        ArgumentNullException.ThrowIfNull(currencyId);

        if (!Enum.IsDefined(basis))
            return Error.Validation($"Unknown pricing basis '{basis}'.");

        if (basis != Basis)
        {
            var revalidate = PricePlanValidator.Validate(basis, _brackets);
            if (revalidate.IsFailure)
                return Error.Conflict(
                    "Cannot change pricing basis without also replacing the current brackets: "
                    + revalidate.Error.Description);
        }

        CurrencyId = currencyId;
        Basis = basis;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ManpowerPricePlanBasicsUpdatedEvent(Id));
        return Result.Success();
    }

    public Result ReplaceBrackets(IEnumerable<PriceBracket> brackets)
    {
        var rows = (brackets ?? []).ToList();
        var validation = PricePlanValidator.Validate(Basis, rows);
        if (validation.IsFailure) return validation.Error;

        _brackets.Clear();
        _brackets.AddRange(rows);
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ManpowerPricePlanBracketsReplacedEvent(Id));
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Manpower price plan is already active.");

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ManpowerPricePlanActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Manpower price plan is already inactive.");

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ManpowerPricePlanDeactivatedEvent(Id));
        return Result.Success();
    }
}
