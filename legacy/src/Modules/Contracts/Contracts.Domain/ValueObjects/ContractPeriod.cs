using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;
using Contracts.Domain.Enumerations;

namespace Contracts.Domain.ValueObjects;

/// <summary>
/// Time window during which the contract is in effect. <see cref="ExpiryAlertDays"/> +
/// <see cref="ExpiryAlertInterval"/> drive the notification job; neither is consulted for
/// status transitions, only for the "expiring soon" alert window.
/// </summary>
public sealed class ContractPeriod : ValueObject
{
    public DateTimeOffset StartDate { get; private set; }
    public DateTimeOffset ExpiryDate { get; private set; }
    public int ExpiryAlertDays { get; private set; }
    public ExpiryAlertInterval? ExpiryAlertInterval { get; private set; }

    private ContractPeriod() { }

    private ContractPeriod(
        DateTimeOffset startDate,
        DateTimeOffset expiryDate,
        int expiryAlertDays,
        ExpiryAlertInterval? expiryAlertInterval)
    {
        StartDate = startDate;
        ExpiryDate = expiryDate;
        ExpiryAlertDays = expiryAlertDays;
        ExpiryAlertInterval = expiryAlertInterval;
    }

    public static Result<ContractPeriod> Create(
        DateTimeOffset startDate,
        DateTimeOffset expiryDate,
        int expiryAlertDays,
        ExpiryAlertInterval? expiryAlertInterval)
    {
        if (expiryDate <= startDate)
            return Error.Validation("Contract expiry date must be after the start date.");

        if (expiryAlertDays < 0)
            return Error.Validation("Expiry alert days cannot be negative.");

        if (expiryAlertDays > 365)
            return Error.Validation("Expiry alert days must not exceed 365.");

        if (expiryAlertDays > 0 && expiryAlertInterval is null)
            return Error.Validation("Expiry alert interval is required when alert days are set.");

        if (expiryAlertDays > 0 && !Enum.IsDefined(expiryAlertInterval!.Value))
            return Error.Validation("Unknown expiry alert interval.");

        return new ContractPeriod(
            startDate,
            expiryDate,
            expiryAlertDays,
            expiryAlertDays > 0 ? expiryAlertInterval : null);
    }

    /// <summary>Period spans the instant <paramref name="now"/>.</summary>
    public bool IsWithin(DateTimeOffset now) => now >= StartDate && now <= ExpiryDate;

    /// <summary>True when expiry falls inside the configured alert window at <paramref name="now"/>.</summary>
    public bool IsInAlertWindow(DateTimeOffset now)
    {
        if (ExpiryAlertDays <= 0) return false;
        if (now >= ExpiryDate) return false;
        return (ExpiryDate - now).TotalDays <= ExpiryAlertDays;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return StartDate;
        yield return ExpiryDate;
        yield return ExpiryAlertDays;
        yield return ExpiryAlertInterval ?? (object)"null";
    }
}
