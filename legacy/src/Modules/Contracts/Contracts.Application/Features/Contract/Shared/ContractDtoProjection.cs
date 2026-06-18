using Contracts.Contracts.Contract;
using ContractAggregate = Contracts.Domain.Aggregates.Contract.Contract;
using CoreCustomer = Core.Contracts.Features.Customer.CustomerSnapshot;
using CoreCurrency = Core.Contracts.Features.Currency.CurrencySnapshot;
using CoreOperationType = Core.Contracts.Features.OperationType.OperationTypeSnapshot;
using CoreStation = Core.Contracts.Features.Station.StationSnapshot;
using CoreService = Core.Contracts.Features.Service.ServiceSnapshot;
using CoreManpowerType = Core.Contracts.Features.ManpowerType.ManpowerTypeSnapshot;
using CoreAircraftType = Core.Contracts.Features.AircraftType.AircraftTypeSnapshot;
using StoreTool = Store.Contracts.Features.Tool.ToolSnapshot;
using StoreMaterial = Store.Contracts.Features.Material.MaterialSnapshot;
using StoreGeneralSupport = Store.Contracts.Features.GeneralSupport.GeneralSupportSnapshot;
using ContractDomainService = Contracts.Domain.ValueObjects.ServiceSnapshot;

namespace Contracts.Application.Features.Contract.Shared;

/// <summary>
/// In-memory projection from the loaded <see cref="Contract"/> aggregate to the public
/// <see cref="ContractDto"/>. Kept out of <c>Select</c> so EF doesn't have to translate the
/// VO unwrap chain — the GetById query loads the aggregate fully then maps.
/// </summary>
internal static class ContractDtoProjection
{
    public static ContractDto ToDto(ContractAggregate aggregate) =>
        new(
            aggregate.Id.Value,
            aggregate.ContractNo.Value,
            new CoreCustomer(aggregate.Customer.CustomerId, aggregate.Customer.IataCode, aggregate.Customer.Name),
            new CoreCurrency(aggregate.Currency.CurrencyId, aggregate.Currency.Code),
            new ContractPeriod(
                aggregate.Period.StartDate,
                aggregate.Period.ExpiryDate,
                aggregate.Period.ExpiryAlertDays,
                aggregate.Period.ExpiryAlertInterval),
            new ContractFees(
                ToFee(aggregate.FeesAndRates.AdminFee),
                ToFee(aggregate.FeesAndRates.DisbursementFee),
                ToFee(aggregate.FeesAndRates.HolidayFee),
                ToFee(aggregate.FeesAndRates.NightFee),
                ToFee(aggregate.FeesAndRates.ReturnToRampDiscount),
                ToFee(aggregate.FeesAndRates.OtherDiscount)),
            aggregate.DebriefRequired,
            aggregate.AdvancePayments
                .Select(ap => new AdvancePayment(
                    ap.Id.Value,
                    ap.OperationTypeId,
                    new CoreOperationType(ap.OperationType.OperationTypeId, ap.OperationType.Name),
                    ap.Payment.FlightsCount,
                    ap.Payment.FlightCost.Amount,
                    ap.Payment.Balance.Amount,
                    ap.Payment.Deposit.Amount,
                    ap.Payment.RemainingBalance.Amount,
                    ap.Payment.RemainingDeposit.Amount))
                .ToList(),
            new CancellationPlan(
                aggregate.CancellationBasis,
                aggregate.CancellationChargeType,
                aggregate.CancellationBrackets
                    .OrderBy(b => b.SortOrder)
                    .Select(b => new PlanBracket(b.MinMinutes, b.MaxMinutes, b.Value, b.SortOrder))
                    .ToList()),
            new DelayPlan(
                aggregate.DelayType,
                aggregate.DelayBasis,
                aggregate.DelayChargeType,
                aggregate.DelayBrackets
                    .OrderBy(b => b.SortOrder)
                    .Select(b => new PlanBracket(b.MinMinutes, b.MaxMinutes, b.Value, b.SortOrder))
                    .ToList()),
            aggregate.Stations
                .Select(s => new CoreStation(s.Station.StationId, s.Station.Name, s.Station.IataCode))
                .ToList(),
            aggregate.OperationTypes
                .Select(o => new ContractOperationTypeDto(
                    new CoreOperationType(o.OperationType.OperationTypeId, o.OperationType.Name),
                    o.Services
                        .Select(s => new CoreService(s.ServiceId, s.Name, s.IsAog))
                        .ToList()))
                .ToList(),
            aggregate.Services.Select(s => new ContractService(
                    s.Id.Value,
                    s.OperationTypeId,
                    s.PackagePaidBalance?.Amount,
                    s.PackageRemainingBalance?.Amount,
                    new CoreService(s.Service.ServiceId, s.Service.Name),
                    s.AircraftType is null
                        ? null
                        : new CoreAircraftType(s.AircraftType.AircraftTypeId, s.AircraftType.Model),
                    new CoreOperationType(s.OperationType.OperationTypeId, s.OperationType.Name),
                    s.Basis,
                    s.Brackets
                        .Select(b => new PriceBracket(
                            b.MinMinutes, b.MaxMinutes, b.BlockSize,
                            b.PriceValue, b.PackagePriceValue, b.BillingMode))
                        .ToList()))
                .ToList(),
            aggregate.Manpowers.Select(m => new ContractManpower(
                    m.Id.Value,
                    m.OperationTypeId,
                    m.PackagePaidBalance?.Amount,
                    m.PackageRemainingBalance?.Amount,
                    new CoreManpowerType(m.ManpowerType.ManpowerTypeId, m.ManpowerType.Name),
                    new CoreOperationType(m.OperationType.OperationTypeId, m.OperationType.Name),
                    m.Basis,
                    m.Brackets
                        .Select(b => new PriceBracket(
                            b.MinMinutes, b.MaxMinutes, b.BlockSize,
                            b.PriceValue, b.PackagePriceValue, b.BillingMode))
                        .ToList()))
                .ToList(),
            aggregate.Tools.Select(t => new ContractTool(
                    t.Id.Value,
                    t.OperationTypeId,
                    t.PackagePaidBalance?.Amount,
                    t.PackageRemainingBalance?.Amount,
                    new StoreTool(t.Tool.ToolId, t.Tool.Name),
                    t.AircraftType is null
                        ? null
                        : new CoreAircraftType(t.AircraftType.AircraftTypeId, t.AircraftType.Model),
                    new CoreOperationType(t.OperationType.OperationTypeId, t.OperationType.Name),
                    t.Basis,
                    t.Brackets
                        .Select(b => new PriceBracket(
                            b.MinMinutes, b.MaxMinutes, b.BlockSize,
                            b.PriceValue, b.PackagePriceValue, b.BillingMode))
                        .ToList()))
                .ToList(),
            aggregate.Materials.Select(m => new ContractMaterial(
                    m.Id.Value,
                    m.OperationTypeId,
                    m.PackagePaidBalance?.Amount,
                    m.PackageRemainingBalance?.Amount,
                    new StoreMaterial(m.Material.MaterialId, m.Material.Name),
                    new CoreOperationType(m.OperationType.OperationTypeId, m.OperationType.Name),
                    m.Basis,
                    m.Brackets
                        .Select(b => new PriceBracket(
                            b.MinMinutes, b.MaxMinutes, b.BlockSize,
                            b.PriceValue, b.PackagePriceValue, b.BillingMode))
                        .ToList()))
                .ToList(),
            aggregate.GeneralSupports.Select(g => new ContractGeneralSupport(
                    g.Id.Value,
                    g.OperationTypeId,
                    g.PackagePaidBalance?.Amount,
                    g.PackageRemainingBalance?.Amount,
                    new StoreGeneralSupport(g.GeneralSupport.GeneralSupportId, g.GeneralSupport.Name),
                    new CoreOperationType(g.OperationType.OperationTypeId, g.OperationType.Name),
                    g.Basis,
                    g.Brackets
                        .Select(b => new PriceBracket(
                            b.MinMinutes, b.MaxMinutes, b.BlockSize,
                            b.PriceValue, b.PackagePriceValue, b.BillingMode))
                        .ToList()))
                .ToList(),
            aggregate.Status,
            aggregate.Termination?.Reason,
            aggregate.Termination?.ByUserId,
            aggregate.Termination?.AtUtc,
            aggregate.PaymentTerms,
            aggregate.ApplyVat,
            aggregate.CreatedByUserId,
            aggregate.CreatedAt,
            aggregate.UpdatedByUserId,
            aggregate.UpdatedAt,
            aggregate.Attachment);

    private static Fee ToFee(global::Contracts.Domain.ValueObjects.Fee fee) =>
        new(fee.Type, fee.Value);
}
