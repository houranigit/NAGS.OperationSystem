using Contracts.Domain.ValueObjects;
using Xunit;

namespace Contracts.Domain.Tests;

public sealed class ScheduledAdvancedPaymentTests
{
    private static ScheduledAdvancedPayment Build(
        decimal balance = 1_000m,
        decimal deposit = 500m,
        decimal flightCost = 100m,
        int flightsCount = 10) =>
        ScheduledAdvancedPayment.Create(
            flightsCount,
            Money.From(flightCost),
            Money.From(balance),
            Money.From(deposit)).Value;

    [Fact]
    public void Create_initialises_remainings_to_full_amounts()
    {
        var payment = Build();

        Assert.Equal(1_000m, payment.RemainingBalance.Amount);
        Assert.Equal(500m, payment.RemainingDeposit.Amount);
        Assert.True(payment.IsUntouched);
    }

    [Fact]
    public void Consume_zero_charge_does_not_mutate_remainings()
    {
        var payment = Build();

        var result = payment.Consume(Money.Zero);

        Assert.True(result.IsSuccess);
        Assert.Equal(1_000m, result.Value.UpdatedPayment.RemainingBalance.Amount);
        Assert.Equal(500m, result.Value.UpdatedPayment.RemainingDeposit.Amount);
        Assert.True(result.Value.FromBalance.IsZero);
        Assert.True(result.Value.FromDeposit.IsZero);
        Assert.True(result.Value.Shortfall.IsZero);
    }

    [Fact]
    public void Consume_within_balance_only_deducts_from_balance()
    {
        var payment = Build();

        var result = payment.Consume(Money.From(300m));

        Assert.True(result.IsSuccess);
        Assert.Equal(700m, result.Value.UpdatedPayment.RemainingBalance.Amount);
        Assert.Equal(500m, result.Value.UpdatedPayment.RemainingDeposit.Amount);
        Assert.Equal(300m, result.Value.FromBalance.Amount);
        Assert.True(result.Value.FromDeposit.IsZero);
        Assert.False(result.Value.BalanceDepleted);
    }

    [Fact]
    public void Consume_overflow_spills_into_deposit_and_marks_balance_depleted()
    {
        var payment = Build(balance: 200m, deposit: 500m);

        var result = payment.Consume(Money.From(350m));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.UpdatedPayment.RemainingBalance.IsZero);
        Assert.Equal(350m, payment.Balance.Amount + payment.Deposit.Amount - result.Value.UpdatedPayment.RemainingBalance.Amount - result.Value.UpdatedPayment.RemainingDeposit.Amount);
        Assert.Equal(200m, result.Value.FromBalance.Amount);
        Assert.Equal(150m, result.Value.FromDeposit.Amount);
        Assert.True(result.Value.BalanceDepleted);
        Assert.False(result.Value.DepositDepleted);
    }

    [Fact]
    public void Consume_exhausts_balance_and_deposit_then_returns_shortfall()
    {
        var payment = Build(balance: 100m, deposit: 50m);

        var result = payment.Consume(Money.From(200m));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.UpdatedPayment.RemainingBalance.IsZero);
        Assert.True(result.Value.UpdatedPayment.RemainingDeposit.IsZero);
        Assert.Equal(100m, result.Value.FromBalance.Amount);
        Assert.Equal(50m, result.Value.FromDeposit.Amount);
        Assert.Equal(50m, result.Value.Shortfall.Amount);
        Assert.True(result.Value.BalanceDepleted);
        Assert.True(result.Value.DepositDepleted);
    }

    [Fact]
    public void Consume_does_not_mutate_the_original_instance()
    {
        var payment = Build(balance: 1_000m, deposit: 500m);

        var result = payment.Consume(Money.From(400m));

        Assert.True(result.IsSuccess);
        Assert.Equal(1_000m, payment.RemainingBalance.Amount);
        Assert.Equal(500m, payment.RemainingDeposit.Amount);
        Assert.Equal(600m, result.Value.UpdatedPayment.RemainingBalance.Amount);
    }

    [Fact]
    public void Rehydrate_disallows_remaining_greater_than_original()
    {
        var rehydrate = ScheduledAdvancedPayment.Rehydrate(
            flightsCount: 5,
            flightCost: Money.From(100m),
            balance: Money.From(500m),
            deposit: Money.From(100m),
            remainingBalance: Money.From(600m),
            remainingDeposit: Money.From(50m));

        Assert.True(rehydrate.IsFailure);
    }
}
