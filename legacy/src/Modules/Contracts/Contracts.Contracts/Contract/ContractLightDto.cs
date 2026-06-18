using Contracts.Domain.Enumerations;
using Core.Contracts.Features.Currency;
using Core.Contracts.Features.Customer;

namespace Contracts.Contracts.Contract;

/// <summary>List/grid row — no child collections, suitable for paginated grids.</summary>
public sealed record ContractSummary(
    Guid Id,
    string ContractNo,
    CustomerSnapshot Customer,
    CurrencySnapshot Currency,
    ContractPeriod Period,
    ContractStatus Status,
    PaymentTerms PaymentTerms,
    bool ApplyVat,
    Guid CreatedByUserId,
    DateTime CreatedAt,
    Guid? UpdatedByUserId,
    DateTime? UpdatedAt);
