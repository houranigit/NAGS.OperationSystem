using Store.Domain.Enumerations;

namespace Store.Contracts.Features.Pricing;

/// <summary>
/// One row of a system-default price plan's ladder, projected for the contract wizard.
/// Mirrors <c>Core.Contracts.Features.Pricing.PriceBracketDto</c> in shape but lives inside
/// the Store module to keep cross-module references at zero.
/// </summary>
public sealed record PriceBracketDto(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal Value,
    BracketBillingMode BillingMode);
