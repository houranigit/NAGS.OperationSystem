using Store.Domain.Enumerations;

namespace Store.Contracts.Features.Pricing;

public sealed record PricePlanBracketInput(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal Value,
    BracketBillingMode BillingMode);
