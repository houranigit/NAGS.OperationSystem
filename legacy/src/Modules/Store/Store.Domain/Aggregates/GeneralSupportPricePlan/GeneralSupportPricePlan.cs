using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.GeneralSupport;
using Store.Domain.Enumerations;
using Store.Domain.Events;
using Store.Domain.Pricing;

namespace Store.Domain.Aggregates.GeneralSupportPricePlan;

/// <summary>
/// System-default price plan for a single <see cref="GeneralSupport"/> — flat shape (one plan
/// per item) and no operation-type / aircraft-type dimensions.
/// </summary>
public sealed class GeneralSupportPricePlan : AggregateRoot<GeneralSupportPricePlanId>
{
    private readonly List<PriceBracket> _brackets = [];

    public GeneralSupportId GeneralSupportId { get; private set; } = null!;
    public Guid CurrencyId { get; private set; }
    public PricingBasis Basis { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyList<PriceBracket> Brackets => _brackets.AsReadOnly();

    private GeneralSupportPricePlan() { }

    public static Result<GeneralSupportPricePlan> Create(
        GeneralSupportId generalSupportId,
        Guid currencyId,
        PricingBasis basis,
        IEnumerable<PriceBracket> brackets)
    {
        ArgumentNullException.ThrowIfNull(generalSupportId);
        if (currencyId == Guid.Empty)
            return Error.Validation("CurrencyId is required.");

        var rows = (brackets ?? []).ToList();
        var validation = PricePlanValidator.Validate(basis, rows);
        if (validation.IsFailure) return validation.Error;

        var plan = new GeneralSupportPricePlan
        {
            Id = GeneralSupportPricePlanId.New(),
            GeneralSupportId = generalSupportId,
            CurrencyId = currencyId,
            Basis = basis,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        plan._brackets.AddRange(rows);

        plan.RaiseDomainEvent(new GeneralSupportPricePlanCreatedEvent(plan.Id));
        return plan;
    }

    public Result UpdateBasics(Guid currencyId, PricingBasis basis)
    {
        if (currencyId == Guid.Empty)
            return Error.Validation("CurrencyId is required.");

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
        RaiseDomainEvent(new GeneralSupportPricePlanBasicsUpdatedEvent(Id));
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
        RaiseDomainEvent(new GeneralSupportPricePlanBracketsReplacedEvent(Id));
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("General support price plan is already active.");

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new GeneralSupportPricePlanActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("General support price plan is already inactive.");

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new GeneralSupportPricePlanDeactivatedEvent(Id));
        return Result.Success();
    }
}
