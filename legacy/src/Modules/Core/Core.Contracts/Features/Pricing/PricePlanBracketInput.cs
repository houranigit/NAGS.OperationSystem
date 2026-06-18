using Core.Domain.Enumerations;

namespace Core.Contracts.Features.Pricing;

public sealed record PricePlanBracketInput(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal Value,
    BracketBillingMode BillingMode);
