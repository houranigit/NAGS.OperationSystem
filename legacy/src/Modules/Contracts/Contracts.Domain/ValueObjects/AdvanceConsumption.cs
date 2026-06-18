namespace Contracts.Domain.ValueObjects;

/// <summary>
/// Result of <see cref="ScheduledAdvancedPayment.Consume"/>: the new payment state plus the
/// amounts deducted from balance and deposit. <see cref="Shortfall"/> &gt; 0 means the
/// charge exhausted both buckets and the residual must be invoiced normally by Billing.
/// </summary>
public sealed record AdvanceConsumption(
    ScheduledAdvancedPayment UpdatedPayment,
    Money FromBalance,
    Money FromDeposit,
    bool BalanceDepleted,
    bool DepositDepleted,
    Money Shortfall);
