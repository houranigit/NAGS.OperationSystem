using Core.Domain.Enumerations;

namespace Core.Contracts.Features.ServicePricePlan;

public sealed record ServicePricePlanBracketDto(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal Value,
    BracketBillingMode BillingMode);
