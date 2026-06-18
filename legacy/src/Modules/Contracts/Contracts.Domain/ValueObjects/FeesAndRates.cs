using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.ValueObjects;

/// <summary>
/// Mandatory bundle of contract-level fees and discounts. All six are required on every
/// contract; set value to 0 when an item should not apply.
/// </summary>
/// <remarks>
/// Discounts and fees share the same shape (Type + Value) so they live together in this VO
/// to keep the database denormalisation simple. Future modules treat AdminFee/Disbursement/
/// Holiday/NightFee as additive surcharges and ReturnToRampDiscount/OtherDiscount as
/// subtractive credits.
/// </remarks>
public sealed class FeesAndRates : ValueObject
{
    public Fee AdminFee { get; private set; } = null!;
    public Fee DisbursementFee { get; private set; } = null!;
    public Fee HolidayFee { get; private set; } = null!;
    public Fee NightFee { get; private set; } = null!;
    public Fee ReturnToRampDiscount { get; private set; } = null!;
    public Fee OtherDiscount { get; private set; } = null!;

    private FeesAndRates() { }

    private FeesAndRates(
        Fee adminFee,
        Fee disbursementFee,
        Fee holidayFee,
        Fee nightFee,
        Fee returnToRampDiscount,
        Fee otherDiscount)
    {
        AdminFee = adminFee;
        DisbursementFee = disbursementFee;
        HolidayFee = holidayFee;
        NightFee = nightFee;
        ReturnToRampDiscount = returnToRampDiscount;
        OtherDiscount = otherDiscount;
    }

    public static Result<FeesAndRates> Create(
        Fee? adminFee,
        Fee? disbursementFee,
        Fee? holidayFee,
        Fee? nightFee,
        Fee? returnToRampDiscount,
        Fee? otherDiscount)
    {
        if (adminFee is null) return Error.Validation("Admin fee is required.");
        if (disbursementFee is null) return Error.Validation("Disbursement fee is required.");
        if (holidayFee is null) return Error.Validation("Holiday fee is required.");
        if (nightFee is null) return Error.Validation("Night fee is required.");
        if (returnToRampDiscount is null) return Error.Validation("Return-to-ramp discount is required.");
        if (otherDiscount is null) return Error.Validation("Other discount is required.");

        return new FeesAndRates(
            adminFee, disbursementFee, holidayFee, nightFee, returnToRampDiscount, otherDiscount);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return AdminFee;
        yield return DisbursementFee;
        yield return HolidayFee;
        yield return NightFee;
        yield return ReturnToRampDiscount;
        yield return OtherDiscount;
    }
}
