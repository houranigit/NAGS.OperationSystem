using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;
using Contracts.Domain.Enumerations;
using Contracts.Domain.Services;
using Contracts.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract.Pricing;

/// <summary>
/// Pricing line for a single (OperationType, Service[, AircraftType]) tuple. Owns the
/// bracket ladder and any pre-paid package balance state. Created and replaced through the
/// <see cref="Contract"/> aggregate only.
/// </summary>
public sealed class ContractService : Entity<ContractServiceId>
{
    public ContractId ContractId { get; private set; } = null!;
    public Guid OperationTypeId { get; private set; }
    public OperationTypeSnapshot OperationType { get; private set; } = null!;
    public ServiceSnapshot Service { get; private set; } = null!;
    public AircraftTypeSnapshot? AircraftType { get; private set; }
    public PricingBasis Basis { get; private set; }
    public Money? PackagePaidBalance { get; private set; }
    public Money? PackageRemainingBalance { get; private set; }

    private readonly List<ContractPriceBracket> _brackets = [];
    public IReadOnlyList<ContractPriceBracket> Brackets => _brackets;

    private ContractService() { }

    /// <summary>Internal factory invoked by <see cref="Contract"/>.</summary>
    internal static Result<ContractService> Create(
        ContractId contractId,
        ContractServiceId? id,
        OperationTypeSnapshot operationType,
        ServiceSnapshot service,
        AircraftTypeSnapshot? aircraftType,
        PricingBasis basis,
        Money? packagePaidBalance,
        IReadOnlyList<ContractPriceBracket> brackets)
    {
        var validation = ContractPricePlanValidator.Validate(basis, brackets, packagePaidBalance is { IsPositive: true });
        if (validation.IsFailure) return validation.Error;

        if (packagePaidBalance is { IsZero: true })
            packagePaidBalance = null;

        var entity = new ContractService
        {
            Id = id ?? ContractServiceId.New(),
            ContractId = contractId,
            OperationType = operationType,
            OperationTypeId = operationType.OperationTypeId,
            Service = service,
            AircraftType = aircraftType,
            Basis = basis,
            PackagePaidBalance = packagePaidBalance,
            // EF owned-entity tracker keys on object identity — sharing the same Money
            // instance with PackagePaidBalance throws "property X belongs to type Y but is
            // being used with an instance of type Z" on save. Clone so each owned
            // navigation has its own reference.
            PackageRemainingBalance = packagePaidBalance is null ? null : Money.From(packagePaidBalance.Amount),
        };
        entity._brackets.AddRange(brackets);
        return entity;
    }

    /// <summary>
    /// Used by <see cref="Contract.Update"/> to preserve the partially-consumed remaining
    /// balance when the line was already in flight.
    /// </summary>
    internal void PreserveRemainingBalance(Money? previousRemaining)
    {
        if (PackagePaidBalance is null) return;
        if (previousRemaining is null) return;
        if (previousRemaining.Amount > PackagePaidBalance.Amount) return;
        // Clone — `previousRemaining` was attached to the old (now-deleted) child entity;
        // reusing the reference would re-introduce the EF "shared owned instance" error.
        PackageRemainingBalance = Money.From(previousRemaining.Amount);
    }

    /// <summary>
    /// Deducts the supplied charge from the remaining package balance. No-op when the line
    /// has no balance. Returns the consumed amount; any shortfall (charge larger than what
    /// remains) is returned to the caller for normal billing.
    /// </summary>
    public Result<Money> ConsumePackage(Money charge)
    {
        if (charge is null)
            return Error.Validation("Charge is required.");
        if (charge.Amount < 0m)
            return Error.Validation("Charge cannot be negative.");

        if (PackagePaidBalance is null || PackageRemainingBalance is null || PackageRemainingBalance.IsZero)
            return Money.Zero;

        var consumedAmount = Math.Min(PackageRemainingBalance.Amount, charge.Amount);
        PackageRemainingBalance = Money.From(PackageRemainingBalance.Amount - consumedAmount);
        return Money.From(consumedAmount);
    }
}
