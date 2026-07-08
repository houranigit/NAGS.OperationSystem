using System.Net;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class FlightEndpointsTests(OperationsApiFactory factory) : IClassFixture<OperationsApiFactory>
{
    [Fact]
    public async Task GetFlights_WithoutAuthentication_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync($"{OperationsApiFactory.Base}/flights");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

}
