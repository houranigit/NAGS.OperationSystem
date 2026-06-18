using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;
using Contracts.Domain.Enumerations;
using Contracts.Domain.Services;
using Contracts.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract.Pricing;

/// <summary>
/// Pricing line for a single (OperationType, ManpowerType) tuple. Same shape as
/// <see cref="ContractService"/> minus the AircraftType dimension.
/// </summary>
public sealed class ContractManpower : Entity<ContractManpowerId>
{
    public ContractId ContractId { get; private set; } = null!;
    public Guid OperationTypeId { get; private set; }
    public OperationTypeSnapshot OperationType { get; private set; } = null!;
    public ManpowerTypeSnapshot ManpowerType { get; private set; } = null!;
    public PricingBasis Basis { get; private set; }
    public Money? PackagePaidBalance { get; private set; }
    public Money? PackageRemainingBalance { get; private set; }

    private readonly List<ContractPriceBracket> _brackets = [];
    public IReadOnlyList<ContractPriceBracket> Brackets => _brackets;

    private ContractManpower() { }

    internal static Result<ContractManpower> Create(
        ContractId contractId,
        ContractManpowerId? id,
        OperationTypeSnapshot operationType,
        ManpowerTypeSnapshot manpowerType,
        PricingBasis basis,
        Money? packagePaidBalance,
        IReadOnlyList<ContractPriceBracket> brackets)
    {
        var validation = ContractPricePlanValidator.Validate(basis, brackets, packagePaidBalance is { IsPositive: true });
        if (validation.IsFailure) return validation.Error;

        if (packagePaidBalance is { IsZero: true })
            packagePaidBalance = null;

        var entity = new ContractManpower
        {
            Id = id ?? ContractManpowerId.New(),
            ContractId = contractId,
            OperationType = operationType,
            OperationTypeId = operationType.OperationTypeId,
            ManpowerType = manpowerType,
            Basis = basis,
            PackagePaidBalance = packagePaidBalance,
            // See ContractService.Create — clone Money so the owned-entity tracker can
            // distinguish PackagePaidBalance from PackageRemainingBalance by reference.
            PackageRemainingBalance = packagePaidBalance is null ? null : Money.From(packagePaidBalance.Amount),
        };
        entity._brackets.AddRange(brackets);
        return entity;
    }

    internal void PreserveRemainingBalance(Money? previousRemaining)
    {
        if (PackagePaidBalance is null) return;
        if (previousRemaining is null) return;
        if (previousRemaining.Amount > PackagePaidBalance.Amount) return;
        // Clone — see ContractService.PreserveRemainingBalance.
        PackageRemainingBalance = Money.From(previousRemaining.Amount);
    }

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
