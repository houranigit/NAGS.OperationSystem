using Store.Domain.Enumerations;

namespace Store.Contracts.Features.MaterialPricePlan;

public sealed record MaterialPricePlanBracketDto(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal Value,
    BracketBillingMode BillingMode);
