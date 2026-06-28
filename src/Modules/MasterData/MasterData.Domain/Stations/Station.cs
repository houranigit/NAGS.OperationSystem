using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Auditing;
using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.Stations;

/// <summary>
/// An airport station the operation works at. Identified by a unique 3-letter IATA code with an
/// optional unique 4-letter ICAO code. References an active <see cref="Countries.Country"/>.
/// Long-lived master data with an active/inactive lifecycle; never hard-deleted.
/// </summary>
public sealed class Station : AggregateRoot<Guid>, IAuditable
{
    private Station() { }

    string IAuditable.AuditEntityType => "Station";
    Guid IAuditable.AuditEntityId => Id;

    public string IataCode { get; private set; } = null!;
    public string? IcaoCode { get; private set; }
    public string Name { get; private set; } = null!;
    public string? City { get; private set; }
    public Guid CountryId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic-concurrency token surfaced to clients as an ETag.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public static Result<Station> Create(
        string? iataCode,
        string? icaoCode,
        string? name,
        string? city,
        Guid countryId,
        DateTimeOffset now,
        Guid? id = null)
    {
        var iataCheck = NormalizeIata(iataCode);
        if (iataCheck.IsFailure)
            return iataCheck.Error;

        var icaoCheck = NormalizeIcao(icaoCode);
        if (icaoCheck.IsFailure)
            return icaoCheck.Error;

        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var cityCheck = ValidateCity(city);
        if (cityCheck.IsFailure)
            return cityCheck.Error;

        if (countryId == Guid.Empty)
            return Error.Validation("A country is required.", "MasterData.Station.CountryRequired");

        return new Station
        {
            Id = id ?? Guid.NewGuid(),
            IataCode = iataCheck.Value,
            IcaoCode = icaoCheck.Value,
            Name = nameCheck.Value,
            City = cityCheck.Value,
            CountryId = countryId,
            IsActive = true,
            CreatedAtUtc = now
        };
    }

    public Result Update(
        string? iataCode,
        string? icaoCode,
        string? name,
        string? city,
        Guid countryId,
        DateTimeOffset now)
    {
        var iataCheck = NormalizeIata(iataCode);
        if (iataCheck.IsFailure)
            return iataCheck.Error;

        var icaoCheck = NormalizeIcao(icaoCode);
        if (icaoCheck.IsFailure)
            return icaoCheck.Error;

        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var cityCheck = ValidateCity(city);
        if (cityCheck.IsFailure)
            return cityCheck.Error;

        if (countryId == Guid.Empty)
            return Error.Validation("A country is required.", "MasterData.Station.CountryRequired");

        IataCode = iataCheck.Value;
        IcaoCode = icaoCheck.Value;
        Name = nameCheck.Value;
        City = cityCheck.Value;
        CountryId = countryId;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result Activate(DateTimeOffset now)
    {
        if (IsActive)
            return Result.Success();

        IsActive = true;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
            return Result.Success();

        IsActive = false;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    private static Result<string> NormalizeIata(string? iataCode)
    {
        if (string.IsNullOrWhiteSpace(iataCode))
            return Error.Validation("IATA code is required.", "MasterData.Station.IataRequired");

        var normalized = iataCode.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsAsciiLetter))
            return Error.Validation("IATA code must be exactly three letters.", "MasterData.Station.IataInvalid");

        return normalized;
    }

    private static Result<string?> NormalizeIcao(string? icaoCode)
    {
        if (string.IsNullOrWhiteSpace(icaoCode))
            return Result.Success<string?>(null);

        var normalized = icaoCode.Trim().ToUpperInvariant();
        if (normalized.Length != 4 || !normalized.All(char.IsAsciiLetter))
            return Error.Validation("ICAO code must be exactly four letters.", "MasterData.Station.IcaoInvalid");

        return Result.Success<string?>(normalized);
    }

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Station name is required.", "MasterData.Station.NameRequired");

        var trimmed = name.Trim();
        if (trimmed.Length > 150)
            return Error.Validation("Station name must be at most 150 characters.", "MasterData.Station.NameTooLong");

        return trimmed;
    }

    private static Result<string?> ValidateCity(string? city)
    {
        if (string.IsNullOrWhiteSpace(city))
            return Result.Success<string?>(null);

        var trimmed = city.Trim();
        if (trimmed.Length > 100)
            return Error.Validation("City must be at most 100 characters.", "MasterData.Station.CityTooLong");

        return Result.Success<string?>(trimmed);
    }
}
