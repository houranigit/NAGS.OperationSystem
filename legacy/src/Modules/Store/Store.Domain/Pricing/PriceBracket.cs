using Store.Domain.Enumerations;

namespace Store.Domain.Pricing;

/// <summary>
/// One row in a Store price-plan ladder. Mirrors <c>Core.Domain.Pricing.PriceBracket</c> but lives
/// inside the Store module so Store.Domain has zero cross-module references.
/// </summary>
public sealed record PriceBracket(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal Value,
    BracketBillingMode BillingMode);
