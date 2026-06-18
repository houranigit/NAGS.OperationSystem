using Core.Domain.Enumerations;

namespace Core.Domain.Pricing;

/// <summary>A single row of the price ladder shared by service/manpower price plans.</summary>
public sealed record PriceBracket(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal Value,
    BracketBillingMode BillingMode);
