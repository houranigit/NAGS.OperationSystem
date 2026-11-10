using MasterData.Domain.Stations;
using Shouldly;

namespace MasterData.Domain.UnitTests.Stations;

public sealed class StationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid CountryId = Guid.NewGuid();

    [Fact]
    public void Create_normalizes_codes_and_trims_text()
    {
        var result = Station.Create("  jed ", " oejn ", "  King Abdulaziz  ", "  Jeddah  ", CountryId, Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IataCode.ShouldBe("JED");
        result.Value.IcaoCode.ShouldBe("OEJN");
        result.Value.Name.ShouldBe("King Abdulaziz");
        result.Value.City.ShouldBe("Jeddah");
        result.Value.CountryId.ShouldBe(CountryId);
        result.Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Create_allows_missing_icao()
    {
        var result = Station.Create("JED", null, "King Abdulaziz", "Jeddah", CountryId, Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IcaoCode.ShouldBeNull();
    }

    [Fact]
    public void Create_allows_missing_city()
    {
        var result = Station.Create("JED", null, "King Abdulaziz", null, CountryId, Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.City.ShouldBeNull();
    }

    [Theory]
    [InlineData("JE")]
    [InlineData("JEDD")]
    [InlineData("J3D")]
    public void Create_with_invalid_iata_fails(string iata)
    {
        var result = Station.Create(iata, null, "Name", "City", CountryId, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBeOneOf("MasterData.Station.IataInvalid", "MasterData.Station.IataRequired");
    }

    [Theory]
    [InlineData("OEJ")]
    [InlineData("OEJNN")]
    [InlineData("OEJ1")]
    public void Create_with_invalid_icao_fails(string icao)
    {
        var result = Station.Create("JED", icao, "Name", "City", CountryId, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.Station.IcaoInvalid");
    }

    [Fact]
    public void Create_without_country_fails()
    {
        var result = Station.Create("JED", null, "Name", "City", Guid.Empty, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.Station.CountryRequired");
    }

    [Fact]
    public void Update_changes_fields_and_stamps_updated()
    {
        var station = Station.Create("JED", null, "Name", "Jeddah", CountryId, Now).Value;
        var newCountry = Guid.NewGuid();

        var result = station.Update("RUH", "OERK", "King Khalid", "Riyadh", newCountry, Now.AddDays(1));

        result.IsSuccess.ShouldBeTrue();
        station.IataCode.ShouldBe("RUH");
        station.IcaoCode.ShouldBe("OERK");
        station.City.ShouldBe("Riyadh");
        station.CountryId.ShouldBe(newCountry);
        station.UpdatedAtUtc.ShouldBe(Now.AddDays(1));
    }

    [Fact]
    public void Legacy_domain_create_uses_generic_iana_time_zone()
    {
        var station = Station.Create("JED", null, "Name", null, CountryId, Now).Value;
        station.TimeZoneId.ShouldBe("Etc/UTC");
    }

    [Fact]
    public void Create_captures_coordinates_and_normalized_iana_time_zone()
    {
        var coordinates = GeoCoordinates.Create(21.679564m, 39.156536m).Value;
        var station = Station.Create("JED", null, "Name", null, CountryId, Now,
            geoCoordinates: coordinates, timeZoneId: " Asia/Riyadh ").Value;

        station.GeoCoordinates.ShouldBe(coordinates);
        station.TimeZoneId.ShouldBe("Asia/Riyadh");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown/Station")]
    [InlineData("Arab Standard Time")]
    public void Create_rejects_invalid_or_non_iana_time_zone(string? timeZoneId)
    {
        var result = Station.Create("JED", null, "Name", null, CountryId, Now, timeZoneId: timeZoneId);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.Station.TimeZoneInvalid");
    }

    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(0, 0)]
    public void Coordinates_accept_boundaries_and_zero(decimal latitude, decimal longitude)
    {
        var coordinates = GeoCoordinates.Create(latitude, longitude);
        coordinates.IsSuccess.ShouldBeTrue();
        coordinates.Value.Latitude.ShouldBe(latitude);
        coordinates.Value.Longitude.ShouldBe(longitude);
    }

    [Theory]
    [InlineData(-90.0001, 0)]
    [InlineData(90.0001, 0)]
    [InlineData(0, -180.0001)]
    [InlineData(0, 180.0001)]
    public void Coordinates_reject_out_of_range_values(decimal latitude, decimal longitude)
    {
        GeoCoordinates.Create(latitude, longitude).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Failed_time_zone_update_preserves_station_fields()
    {
        var station = Station.Create("JED", null, "Name", null, CountryId, Now,
            timeZoneId: "Asia/Riyadh").Value;

        station.Update("ORD", null, "Changed", null, CountryId, Now.AddDays(1),
            timeZoneId: "Unknown/Station").IsFailure.ShouldBeTrue();

        station.IataCode.ShouldBe("JED");
        station.TimeZoneId.ShouldBe("Asia/Riyadh");
        station.UpdatedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Deactivate_then_activate_toggles_state()
    {
        var station = Station.Create("JED", null, "Name", "City", CountryId, Now).Value;

        station.Deactivate(Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        station.IsActive.ShouldBeFalse();

        station.Activate(Now.AddDays(2)).IsSuccess.ShouldBeTrue();
        station.IsActive.ShouldBeTrue();
    }
}
