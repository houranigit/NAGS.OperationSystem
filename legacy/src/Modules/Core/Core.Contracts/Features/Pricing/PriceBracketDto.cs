using Core.Domain.Enumerations;

namespace Core.Contracts.Features.Pricing;

/// <summary>
/// One row of a system-default price plan's ladder, projected for the contract wizard.
/// Mirrors <see cref="ServicePricePlan.ServicePricePlanBracketDto"/> but lives under the
/// shared Pricing namespace so manpower / service queries can return the same shape.
/// </summary>
public sealed record PriceBracketDto(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal Value,
    BracketBillingMode BillingMode);
