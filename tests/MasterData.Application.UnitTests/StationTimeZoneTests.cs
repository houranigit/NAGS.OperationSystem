using MasterData.Application.Features.Stations;
using MasterData.Domain.Stations;
using Shouldly;

namespace MasterData.Application.UnitTests;

public sealed class StationTimeZoneTests
{
    [Fact]
    public async Task Station_picker_exposes_unique_sorted_iana_ids_that_the_domain_accepts()
    {
        var result = await new GetStationTimeZonesQueryHandler().Handle(new GetStationTimeZonesQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var options = result.Value;
        options.ShouldContain(option => option.Id == "Asia/Riyadh");
        options.ShouldContain(option => option.Id == "Etc/UTC");
        options.ShouldContain(option => option.Id == "UTC");
        options.Select(option => option.Id).ShouldBe(options.Select(option => option.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        foreach (var option in options)
        {
            option.DisplayName.ShouldContain(option.Id);
            Station.Create("JED", null, "Station", null, Guid.NewGuid(), TimeProvider.System.GetUtcNow(),
                timeZoneId: option.Id).IsSuccess.ShouldBeTrue(option.Id);
        }
    }
}
