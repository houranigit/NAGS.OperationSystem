using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.AircraftType;
using Core.Domain.Aggregates.Currency;
using Core.Domain.Aggregates.OperationType;
using Core.Domain.Aggregates.Service;
using Core.Domain.Events;
using Core.Domain.Enumerations;
using Core.Domain.Pricing;

namespace Core.Domain.Aggregates.ServicePricePlan;

/// <summary>
/// System-default price plan for a given (Service, OperationType, AircraftType?) triple.
/// <see cref="AircraftTypeId"/> is nullable; null means "applies to all aircraft types that
/// do not have a specific plan defined". The Operations/Billing module resolves a plan by
/// preferring the specific AircraftType first, then falling back to the null-aircraft plan.
/// </summary>
public sealed class ServicePricePlan : AggregateRoot<ServicePricePlanId>
{
    private readonly List<PriceBracket> _brackets = [];

    public ServiceId ServiceId { get; private set; } = null!;
    public OperationTypeId OperationTypeId { get; private set; } = null!;
    public AircraftTypeId? AircraftTypeId { get; private set; }
    public CurrencyId CurrencyId { get; private set; } = null!;
    public PricingBasis Basis { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyList<PriceBracket> Brackets => _brackets.AsReadOnly();

    private ServicePricePlan() { }

    public static Result<ServicePricePlan> Create(
        ServiceId serviceId,
        OperationTypeId operationTypeId,
        AircraftTypeId? aircraftTypeId,
        CurrencyId currencyId,
        PricingBasis basis,
        IEnumerable<PriceBracket> brackets)
    {
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(operationTypeId);
        ArgumentNullException.ThrowIfNull(currencyId);

        var rows = (brackets ?? []).ToList();
        var validation = PricePlanValidator.Validate(basis, rows);
        if (validation.IsFailure) return validation.Error;

        var plan = new ServicePricePlan
        {
            Id = ServicePricePlanId.New(),
            ServiceId = serviceId,
            OperationTypeId = operationTypeId,
            AircraftTypeId = aircraftTypeId,
            CurrencyId = currencyId,
            Basis = basis,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        plan._brackets.AddRange(rows);

        plan.RaiseDomainEvent(new ServicePricePlanCreatedEvent(plan.Id));
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
        RaiseDomainEvent(new ServicePricePlanBasicsUpdatedEvent(Id));
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
        RaiseDomainEvent(new ServicePricePlanBracketsReplacedEvent(Id));
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Service price plan is already active.");

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ServicePricePlanActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Service price plan is already inactive.");

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ServicePricePlanDeactivatedEvent(Id));
        return Result.Success();
    }
}
