using Contracts.Domain.Enumerations;

namespace Contracts.Contracts.Contract;

public sealed record ContractPeriod(
    DateTimeOffset StartDate,
    DateTimeOffset ExpiryDate,
    int ExpiryAlertDays,
    ExpiryAlertInterval? ExpiryAlertInterval);
