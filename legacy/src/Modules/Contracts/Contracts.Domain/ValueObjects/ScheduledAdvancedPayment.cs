using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.ValueObjects;

/// <summary>
/// Prepaid flight package with a primary <see cref="Balance"/> and a secured
/// <see cref="Deposit"/>. Charges deduct from <see cref="RemainingBalance"/> first; any
/// overflow spills into <see cref="RemainingDeposit"/> in the same call (sequential-strict).
/// </summary>
public sealed class ScheduledAdvancedPayment : ValueObject
{
    public int FlightsCount { get; private set; }
    public Money FlightCost { get; private set; } = null!;
    public Money Balance { get; private set; } = null!;
    public Money Deposit { get; private set; } = null!;
    public Money RemainingBalance { get; private set; } = null!;
    public Money RemainingDeposit { get; private set; } = null!;

    private ScheduledAdvancedPayment() { }

    private ScheduledAdvancedPayment(
        int flightsCount,
        Money flightCost,
        Money balance,
        Money deposit,
        Money remainingBalance,
        Money remainingDeposit)
    {
        FlightsCount = flightsCount;
        FlightCost = flightCost;
        Balance = balance;
        Deposit = deposit;
        RemainingBalance = remainingBalance;
        RemainingDeposit = remainingDeposit;
    }

    /// <summary>
    /// Creates an initial payment. All inputs are required together — a "no advance payment"
    /// case is represented by passing <c>null</c> to the aggregate setter, not by partial fields.
    /// </summary>
    public static Result<ScheduledAdvancedPayment> Create(
        int flightsCount,
        Money? flightCost,
        Money? balance,
        Money? deposit)
    {
        if (flightCost is null) return Error.Validation("Flight cost is required.");
        if (balance is null) return Error.Validation("Balance is required.");
        if (deposit is null) return Error.Validation("Deposit is required.");

        if (flightsCount <= 0)
            return Error.Validation("Flights count must be greater than zero.");

        if (!flightCost.IsPositive)
            return Error.Validation("Flight cost must be greater than zero.");

        if (!balance.IsPositive)
            return Error.Validation("Balance must be greater than zero.");

        // EF Core's owned-entity tracker keys on object identity. Sharing the same
        // <see cref="Money"/> instance between Balance + RemainingBalance (or Deposit +
        // RemainingDeposit) makes EF throw "property 'X' belongs to type
        // ScheduledAdvancedPayment.RemainingBalance#Money but is being used with an
        // instance of type ScheduledAdvancedPayment.Balance#Money" on save. Clone the
        // value so each owned navigation owns its own reference.
        return new ScheduledAdvancedPayment(
            flightsCount,
            flightCost,
            balance,
            deposit,
            remainingBalance: Money.From(balance.Amount),
            remainingDeposit: Money.From(deposit.Amount));
    }

    /// <summary>EF / test hook to rehydrate an existing payment with current remaining amounts.</summary>
    public static Result<ScheduledAdvancedPayment> Rehydrate(
        int flightsCount,
        Money flightCost,
        Money balance,
        Money deposit,
        Money remainingBalance,
        Money remainingDeposit)
    {
        if (flightsCount <= 0)
            return Error.Validation("Flights count must be greater than zero.");
        if (remainingBalance.Amount > balance.Amount)
            return Error.Validation("Remaining balance cannot exceed the original balance.");
        if (remainingDeposit.Amount > deposit.Amount)
            return Error.Validation("Remaining deposit cannot exceed the original deposit.");

        return new ScheduledAdvancedPayment(
            flightsCount, flightCost, balance, deposit, remainingBalance, remainingDeposit);
    }

    /// <summary>True when no charge has ever been applied.</summary>
    public bool IsUntouched => RemainingBalance == Balance && RemainingDeposit == Deposit;

    /// <summary>
    /// Applies <paramref name="charge"/> to the payment. Deducts from balance first then
    /// from deposit. When deposit cannot cover the rest the residual surfaces as
    /// <see cref="AdvanceConsumption.Shortfall"/>; neither bucket goes negative.
    /// </summary>
    public Result<AdvanceConsumption> Consume(Money charge)
    {
        if (charge.Amount < 0m)
            return Error.Validation("Charge cannot be negative.");

        if (charge.IsZero)
        {
            return new AdvanceConsumption(
                UpdatedPayment: this,
                FromBalance: Money.Zero,
                FromDeposit: Money.Zero,
                BalanceDepleted: false,
                DepositDepleted: false,
                Shortfall: Money.Zero);
        }

        var balanceBefore = RemainingBalance.Amount;
        var depositBefore = RemainingDeposit.Amount;
        var chargeAmount = charge.Amount;

        var fromBalanceAmount = Math.Min(balanceBefore, chargeAmount);
        chargeAmount -= fromBalanceAmount;

        var fromDepositAmount = Math.Min(depositBefore, chargeAmount);
        chargeAmount -= fromDepositAmount;

        var shortfallAmount = chargeAmount;

        var newRemainingBalance = Money.From(balanceBefore - fromBalanceAmount);
        var newRemainingDeposit = Money.From(depositBefore - fromDepositAmount);

        var balanceDepleted = balanceBefore > 0m && newRemainingBalance.IsZero;
        var depositDepleted = depositBefore > 0m && newRemainingDeposit.IsZero && fromDepositAmount > 0m;

        var updated = new ScheduledAdvancedPayment(
            FlightsCount, FlightCost, Balance, Deposit, newRemainingBalance, newRemainingDeposit);

        return new AdvanceConsumption(
            UpdatedPayment: updated,
            FromBalance: Money.From(fromBalanceAmount),
            FromDeposit: Money.From(fromDepositAmount),
            BalanceDepleted: balanceDepleted,
            DepositDepleted: depositDepleted,
            Shortfall: Money.From(shortfallAmount));
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return FlightsCount;
        yield return FlightCost;
        yield return Balance;
        yield return Deposit;
        yield return RemainingBalance;
        yield return RemainingDeposit;
    }
}
