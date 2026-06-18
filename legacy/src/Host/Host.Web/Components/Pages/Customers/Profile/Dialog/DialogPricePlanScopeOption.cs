using BracketBillingMode = Contracts.Domain.Enumerations.BracketBillingMode;
using PricingBasis = Contracts.Domain.Enumerations.PricingBasis;

namespace Host.Web.Components.Pages.Customers.Profile.Dialog;

/// <summary>
/// Host-neutral projection of a single system-default price plan, normalised onto the
/// Contracts module's <see cref="PricingBasis"/> / <see cref="BracketBillingMode"/> enum
/// types so the contract wizard does not have to juggle Core vs Store flavours of those
/// enums when seeding defaults. Built from <c>Core.Contracts.Features.Pricing.PricePlanScopeOption</c>
/// and its Store sibling via <see cref="From"/>.
/// </summary>
public sealed record DialogPricePlanScopeOption(
    Guid PlanId,
    Guid OperationTypeId,
    Guid? AircraftTypeId,
    Guid CurrencyId,
    PricingBasis Basis,
    IReadOnlyList<DialogPriceBracketDto> Brackets)
{
    public static DialogPricePlanScopeOption From(Core.Contracts.Features.Pricing.PricePlanScopeOption src) =>
        new(
            src.PlanId,
            src.OperationTypeId,
            src.AircraftTypeId,
            src.CurrencyId,
            (PricingBasis)src.Basis,
            src.Brackets
                .Select(b => new DialogPriceBracketDto(
                    b.MinMinutes,
                    b.MaxMinutes,
                    b.BlockSize,
                    b.Value,
                    (BracketBillingMode)b.BillingMode))
                .ToList());

    public static DialogPricePlanScopeOption From(Store.Contracts.Features.Pricing.PricePlanScopeOption src) =>
        new(
            src.PlanId,
            src.OperationTypeId,
            src.AircraftTypeId,
            src.CurrencyId,
            (PricingBasis)src.Basis,
            src.Brackets
                .Select(b => new DialogPriceBracketDto(
                    b.MinMinutes,
                    b.MaxMinutes,
                    b.BlockSize,
                    b.Value,
                    (BracketBillingMode)b.BillingMode))
                .ToList());
}

/// <summary>One row of a <see cref="DialogPricePlanScopeOption"/>'s ladder, host-neutral.</summary>
public sealed record DialogPriceBracketDto(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal Value,
    BracketBillingMode BillingMode);
