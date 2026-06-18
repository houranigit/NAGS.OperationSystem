using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.Tool;
using Store.Domain.Enumerations;
using Store.Domain.Events;
using Store.Domain.Pricing;

namespace Store.Domain.Aggregates.ToolPricePlan;

/// <summary>
/// System-default price plan for a single <see cref="Tool"/>. Flat shape — one plan per tool —
/// no operation-type or aircraft-type dimensions (matches the Store module's first-phase scope).
/// </summary>
public sealed class ToolPricePlan : AggregateRoot<ToolPricePlanId>
{
    private readonly List<PriceBracket> _brackets = [];

    public ToolId ToolId { get; private set; } = null!;

    /// <summary>Cross-module Currency reference stored as a raw <see cref="Guid"/> (no Core.Domain dep).</summary>
    public Guid CurrencyId { get; private set; }

    public PricingBasis Basis { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyList<PriceBracket> Brackets => _brackets.AsReadOnly();

    private ToolPricePlan() { }

    public static Result<ToolPricePlan> Create(
        ToolId toolId,
        Guid currencyId,
        PricingBasis basis,
        IEnumerable<PriceBracket> brackets)
    {
        ArgumentNullException.ThrowIfNull(toolId);
        if (currencyId == Guid.Empty)
            return Error.Validation("CurrencyId is required.");

        var rows = (brackets ?? []).ToList();
        var validation = PricePlanValidator.Validate(basis, rows);
        if (validation.IsFailure) return validation.Error;

        var plan = new ToolPricePlan
        {
            Id = ToolPricePlanId.New(),
            ToolId = toolId,
            CurrencyId = currencyId,
            Basis = basis,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        plan._brackets.AddRange(rows);

        plan.RaiseDomainEvent(new ToolPricePlanCreatedEvent(plan.Id));
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
        RaiseDomainEvent(new ToolPricePlanBasicsUpdatedEvent(Id));
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
        RaiseDomainEvent(new ToolPricePlanBracketsReplacedEvent(Id));
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Tool price plan is already active.");

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ToolPricePlanActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Tool price plan is already inactive.");

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ToolPricePlanDeactivatedEvent(Id));
        return Result.Success();
    }
}
