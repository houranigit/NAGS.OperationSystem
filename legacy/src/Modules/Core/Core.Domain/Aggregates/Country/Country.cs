using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Core.Domain.Events;
using Core.Domain.ValueObjects;

namespace Core.Domain.Aggregates.Country;

public sealed class Country : AggregateRoot<CountryId>
{
    public CountryCode Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Country() { }

    public static Result<Country> Create(CountryCode code, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Country name is required.");

        if (name.Length > 100)
            return Error.Validation("Country name must not exceed 100 characters.");

        var country = new Country
        {
            Id = CountryId.New(),
            Code = code,
            Name = name.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        country.RaiseDomainEvent(new CountryCreatedEvent(country.Id));
        return country;
    }

    /// <summary>
    /// Creates a country with a predetermined ID for deterministic data seeding.
    /// Does not raise domain events — not a business operation.
    /// </summary>
    public static Country CreateSeed(Guid id, CountryCode code, string name)
    {
        return new Country
        {
            Id = CountryId.From(id),
            Code = code,
            Name = name.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Country is already active.");

        IsActive = true;
        RaiseDomainEvent(new CountryActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Country is already inactive.");

        IsActive = false;
        RaiseDomainEvent(new CountryDeactivatedEvent(Id));
        return Result.Success();
    }

    public Result UpdateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Country name is required.");

        if (name.Length > 100)
            return Error.Validation("Country name must not exceed 100 characters.");

        Name = name.Trim();
        return Result.Success();
    }
}
