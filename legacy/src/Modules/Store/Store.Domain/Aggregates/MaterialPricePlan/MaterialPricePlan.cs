using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.Material;
using Store.Domain.Enumerations;
using Store.Domain.Events;
using Store.Domain.Pricing;

namespace Store.Domain.Aggregates.MaterialPricePlan;

/// <summary>
/// System-default price plan for a single <see cref="Material"/> — flat shape (one plan per
/// material) and no operation-type / aircraft-type dimensions.
/// </summary>
public sealed class MaterialPricePlan : AggregateRoot<MaterialPricePlanId>
{
    private readonly List<PriceBracket> _brackets = [];

    public MaterialId MaterialId { get; private set; } = null!;
    public Guid CurrencyId { get; private set; }
    public PricingBasis Basis { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyList<PriceBracket> Brackets => _brackets.AsReadOnly();

    private MaterialPricePlan() { }

    public static Result<MaterialPricePlan> Create(
        MaterialId materialId,
        Guid currencyId,
        PricingBasis basis,
        IEnumerable<PriceBracket> brackets)
    {
        ArgumentNullException.ThrowIfNull(materialId);
        if (currencyId == Guid.Empty)
            return Error.Validation("CurrencyId is required.");

        var rows = (brackets ?? []).ToList();
        var validation = PricePlanValidator.Validate(basis, rows);
        if (validation.IsFailure) return validation.Error;

        var plan = new MaterialPricePlan
        {
            Id = MaterialPricePlanId.New(),
            MaterialId = materialId,
            CurrencyId = currencyId,
            Basis = basis,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        plan._brackets.AddRange(rows);

        plan.RaiseDomainEvent(new MaterialPricePlanCreatedEvent(plan.Id));
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
        RaiseDomainEvent(new MaterialPricePlanBasicsUpdatedEvent(Id));
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
        RaiseDomainEvent(new MaterialPricePlanBracketsReplacedEvent(Id));
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Material price plan is already active.");

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new MaterialPricePlanActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Material price plan is already inactive.");

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new MaterialPricePlanDeactivatedEvent(Id));
        return Result.Success();
    }
}
