using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace MasterData.Domain.Stations;

/// <summary>A WGS 84 latitude/longitude pair in decimal degrees.</summary>
public sealed class GeoCoordinates : ValueObject
{
    private GeoCoordinates() { }

    public decimal Latitude { get; private set; }
    public decimal Longitude { get; private set; }

    public static Result<GeoCoordinates> Create(decimal latitude, decimal longitude)
    {
        if (latitude is < -90 or > 90)
            return Error.Validation("Latitude must be between -90 and 90.", "MasterData.Station.LatitudeInvalid");
        if (longitude is < -180 or > 180)
            return Error.Validation("Longitude must be between -180 and 180.", "MasterData.Station.LongitudeInvalid");

        return new GeoCoordinates { Latitude = latitude, Longitude = longitude };
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }
}
