using Contracts.Domain.Enumerations;
using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Service;
using Store.Contracts.Features.GeneralSupport;
using Store.Contracts.Features.Material;
using Store.Contracts.Features.Tool;

namespace Contracts.Contracts.Contract;

public sealed record PriceBracket(
    int MinMinutes,
    int? MaxMinutes,
    int BlockSize,
    decimal PriceValue,
    decimal? PackagePriceValue,
    BracketBillingMode BillingMode);

public sealed record ContractService(
    Guid Id,
    Guid OperationTypeId,
    decimal? PackagePaidBalance,
    decimal? PackageRemainingBalance,
    ServiceSnapshot Service,
    AircraftTypeSnapshot? AircraftType,
    OperationTypeSnapshot OperationType,
    PricingBasis Basis,
    IReadOnlyList<PriceBracket> Brackets);

public sealed record ContractManpower(
    Guid Id,
    Guid OperationTypeId,
    decimal? PackagePaidBalance,
    decimal? PackageRemainingBalance,
    ManpowerTypeSnapshot ManpowerType,
    OperationTypeSnapshot OperationType,
    PricingBasis Basis,
    IReadOnlyList<PriceBracket> Brackets);

public sealed record ContractTool(
    Guid Id,
    Guid OperationTypeId,
    decimal? PackagePaidBalance,
    decimal? PackageRemainingBalance,
    ToolSnapshot Tool,
    AircraftTypeSnapshot? AircraftType,
    OperationTypeSnapshot OperationType,
    PricingBasis Basis,
    IReadOnlyList<PriceBracket> Brackets);

public sealed record ContractMaterial(
    Guid Id,
    Guid OperationTypeId,
    decimal? PackagePaidBalance,
    decimal? PackageRemainingBalance,
    MaterialSnapshot Material,
    OperationTypeSnapshot OperationType,
    PricingBasis Basis,
    IReadOnlyList<PriceBracket> Brackets);

public sealed record ContractGeneralSupport(
    Guid Id,
    Guid OperationTypeId,
    decimal? PackagePaidBalance,
    decimal? PackageRemainingBalance,
    GeneralSupportSnapshot GeneralSupport,
    OperationTypeSnapshot OperationType,
    PricingBasis Basis,
    IReadOnlyList<PriceBracket> Brackets);
