using Contracts.Domain.Enumerations;

namespace Contracts.Domain.Aggregates.Contract.Pricing;

/// <summary>
/// Single row of a contract pricing ladder. Differs from <c>Core.Domain.Pricing.PriceBracket</c>
/// in two ways:
/// <list type="bullet">
/// <item><description><see cref="PackagePriceValue"/> — optional special price applied while
/// the parent line still has remaining pre-paid balance.</description></item>
/// <item><description>The validator (<see cref="Services.ContractPricePlanValidator"/>) allows
/// waiver gaps between rows.</description></item>
/// </list>
/// </summary>
public sealed record ContractPriceBracket(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal PriceValue,
    decimal? PackagePriceValue,
    BracketBillingMode BillingMode);
