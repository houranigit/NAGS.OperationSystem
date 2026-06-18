using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.ValueObjects;

/// <summary>
/// Point-in-time copy of a Core customer captured at contract create / customer replace.
/// Never auto-synced so historical values stay stable for billing and audits.
/// </summary>
public sealed class CustomerSnapshot : ValueObject
{
    public Guid CustomerId { get; private set; }
    public string IataCode { get; private set; } = null!;
    public string Name { get; private set; } = null!;

    private CustomerSnapshot() { }

    private CustomerSnapshot(Guid customerId, string iataCode, string name)
    {
        CustomerId = customerId;
        IataCode = iataCode;
        Name = name;
    }

    public static Result<CustomerSnapshot> Create(Guid customerId, string? iataCode, string? name)
    {
        if (customerId == Guid.Empty)
            return Error.Validation("CustomerId is required.");

        if (string.IsNullOrWhiteSpace(iataCode))
            return Error.Validation("Customer IATA code is required.");

        var normalizedIata = iataCode.Trim().ToUpperInvariant();
        if (normalizedIata.Length != 2)
            return Error.Validation("Customer IATA code must be exactly 2 characters.");

        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Customer name is required.");

        if (name.Length > 200)
            return Error.Validation("Customer name must not exceed 200 characters.");

        return new CustomerSnapshot(customerId, normalizedIata, name.Trim());
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return CustomerId;
        yield return IataCode;
        yield return Name;
    }
}
