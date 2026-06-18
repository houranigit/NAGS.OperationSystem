using BuildingBlocks.Domain.Results;
using Contracts.Domain.Aggregates.Contract;
using Contracts.Domain.Aggregates.Contract.Pricing;
using Contracts.Domain.ValueObjects;
using ContractsDomain = Contracts.Domain;

namespace Contracts.Application.Features.Contract.Shared;

/// <summary>
/// Pure conversion helpers from wizard input records to domain value objects / drafts. Each
/// returns a <see cref="Result"/>: validation lives in the VO factories, the mappers just
/// surface the first failure.
/// </summary>
internal static class DomainMappers
{
    public static Result<ContractPeriod> ToContractPeriod(ContractPeriodInput input) =>
        ContractPeriod.Create(input.StartDate, input.ExpiryDate, input.ExpiryAlertDays, input.ExpiryAlertInterval);

    public static Result<Fee> ToFee(FeeInput input) =>
        Fee.Create(input.Type, input.Value);

    public static Result<FeesAndRates> ToFeesAndRates(FeesAndRatesInput input)
    {
        var admin = ToFee(input.AdminFee);
        if (admin.IsFailure) return admin.Error;
        var disb = ToFee(input.DisbursementFee);
        if (disb.IsFailure) return disb.Error;
        var holiday = ToFee(input.HolidayFee);
        if (holiday.IsFailure) return holiday.Error;
        var night = ToFee(input.NightFee);
        if (night.IsFailure) return night.Error;
        var ramp = ToFee(input.ReturnToRampDiscount);
        if (ramp.IsFailure) return ramp.Error;
        var other = ToFee(input.OtherDiscount);
        if (other.IsFailure) return other.Error;

        return FeesAndRates.Create(admin.Value, disb.Value, holiday.Value, night.Value, ramp.Value, other.Value);
    }

    /// <summary>
    /// Converts a wizard advance-payment input list into the per-OT
    /// <see cref="ContractAdvancePaymentDraft"/> records the aggregate consumes. A
    /// <c>null</c> input is treated as "no advance payments" — same as an empty list.
    /// Per-row money sanity is delegated to <see cref="Money.Create"/>; the aggregate
    /// re-validates uniqueness, OT-on-contract, and AdHoc-rejection rules.
    /// </summary>
    public static Result<IReadOnlyList<ContractAdvancePaymentDraft>> ToAdvancePayments(
        IReadOnlyList<AdvancePaymentInput>? inputs)
    {
        if (inputs is null || inputs.Count == 0)
            return Result<IReadOnlyList<ContractAdvancePaymentDraft>>.Success(Array.Empty<ContractAdvancePaymentDraft>());

        var drafts = new List<ContractAdvancePaymentDraft>(inputs.Count);
        foreach (var input in inputs)
        {
            if (input is null)
                return Error.Validation("Advance payment entries cannot be null.");

            // Money.Create just guards non-negative — the aggregate's own VO factory
            // (called inside ContractAdvancePaymentDraft → ScheduledAdvancedPayment.Create)
            // applies the stricter "balance > 0" / "flightCost > 0" rules.
            var flightCost = Money.Create(input.FlightCost);
            if (flightCost.IsFailure) return flightCost.Error;
            var balance = Money.Create(input.Balance);
            if (balance.IsFailure) return balance.Error;
            var deposit = Money.Create(input.Deposit);
            if (deposit.IsFailure) return deposit.Error;

            drafts.Add(new ContractAdvancePaymentDraft(
                input.OperationTypeId,
                input.FlightsCount,
                input.FlightCost,
                input.Balance,
                input.Deposit,
                input.ExistingContractAdvancePaymentId));
        }
        return Result<IReadOnlyList<ContractAdvancePaymentDraft>>.Success(drafts);
    }

    public static Result<CancellationChargePlan> ToCancellationPlan(CancellationPlanInput input)
    {
        var rows = input.Brackets?.Select(b => new CancellationBracketRow(b.MinMinutes, b.MaxMinutes, b.Value)).ToList()
                   ?? [];
        return CancellationChargePlan.Create(input.Basis, input.ChargeType, rows);
    }

    public static Result<DelayChargePlan> ToDelayPlan(DelayPlanInput input)
    {
        var rows = input.Brackets?.Select(b => new DelayBracketRow(b.MinMinutes, b.MaxMinutes, b.Value)).ToList()
                   ?? [];
        return DelayChargePlan.Create(input.DelayType, input.Basis, input.ChargeType, rows);
    }

    public static Result<IReadOnlyList<ContractPriceBracket>> ToBrackets(
        IReadOnlyList<PriceBracketInput> brackets)
    {
        if (brackets is null || brackets.Count == 0)
            return Error.Validation("Pricing line requires at least 1 bracket.");

        var built = new List<ContractPriceBracket>(brackets.Count);
        foreach (var b in brackets)
        {
            built.Add(new ContractPriceBracket(
                b.MinMinutes, b.MaxMinutes, b.BlockSize, b.PriceValue, b.PackagePriceValue, b.BillingMode));
        }
        return Result<IReadOnlyList<ContractPriceBracket>>.Success(built);
    }

    public static Result<Money?> ToOptionalMoney(decimal? amount)
    {
        if (amount is null) return (Money?)null;
        var built = Money.Create(amount.Value);
        if (built.IsFailure) return built.Error;
        return built.Value;
    }
}
