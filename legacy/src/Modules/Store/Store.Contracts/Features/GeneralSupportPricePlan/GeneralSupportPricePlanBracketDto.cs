using Store.Domain.Enumerations;

namespace Store.Contracts.Features.GeneralSupportPricePlan;

public sealed record GeneralSupportPricePlanBracketDto(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal Value,
    BracketBillingMode BillingMode);
