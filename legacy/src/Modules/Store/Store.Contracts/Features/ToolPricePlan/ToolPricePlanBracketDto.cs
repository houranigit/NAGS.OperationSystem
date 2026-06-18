using Store.Domain.Enumerations;

namespace Store.Contracts.Features.ToolPricePlan;

public sealed record ToolPricePlanBracketDto(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal Value,
    BracketBillingMode BillingMode);
