using Core.Domain.Enumerations;

namespace Core.Contracts.Features.ManpowerPricePlan;

public sealed record ManpowerPricePlanBracketDto(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal Value,
    BracketBillingMode BillingMode);
