using Contracts.Domain.Enumerations;

namespace Contracts.Contracts.Contract;

public sealed record ContractFees(
    Fee AdminFee,
    Fee DisbursementFee,
    Fee HolidayFee,
    Fee NightFee,
    Fee ReturnToRampDiscount,
    Fee OtherDiscount);

public sealed record Fee(FeeType Type, decimal Value);
