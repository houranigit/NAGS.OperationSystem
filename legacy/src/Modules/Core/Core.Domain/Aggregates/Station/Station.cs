using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Country;
using Core.Domain.Events;
using Core.Domain.ValueObjects;

namespace Core.Domain.Aggregates.Station;

public sealed class Station : AggregateRoot<StationId>
{
    public AirportCode IataCode { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public CountryId CountryId { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Station() { }

    public static Result<Station> Create(
        AirportCode iataCode,
        string name,
        string city,
        CountryId countryId)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Station name is required.");

        if (name.Length > 150)
            return Error.Validation("Station name must not exceed 150 characters.");

        if (string.IsNullOrWhiteSpace(city))
            return Error.Validation("City is required.");

        if (city.Length > 100)
            return Error.Validation("City must not exceed 100 characters.");

        var station = new Station
        {
            Id = StationId.New(),
            IataCode = iataCode,
            Name = name.Trim(),
            City = city.Trim(),
            CountryId = countryId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        station.RaiseDomainEvent(new StationCreatedEvent(station.Id));
        return station;
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Station is already active.");

        IsActive = true;
        RaiseDomainEvent(new StationActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Station is already inactive.");

        IsActive = false;
        RaiseDomainEvent(new StationDeactivatedEvent(Id));
        return Result.Success();
    }

    public Result UpdateDetails(string name, string city)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Station name is required.");

        if (name.Length > 150)
            return Error.Validation("Station name must not exceed 150 characters.");

        if (string.IsNullOrWhiteSpace(city))
            return Error.Validation("City is required.");

        if (city.Length > 100)
            return Error.Validation("City must not exceed 100 characters.");

        Name = name.Trim();
        City = city.Trim();
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }
}
