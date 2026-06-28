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
    public void Deactivate_then_activate_toggles_state()
    {
        var station = Station.Create("JED", null, "Name", "City", CountryId, Now).Value;

        station.Deactivate(Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        station.IsActive.ShouldBeFalse();

        station.Activate(Now.AddDays(2)).IsSuccess.ShouldBeTrue();
        station.IsActive.ShouldBeTrue();
    }
}
