using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class FlightEndpointsTests(OperationsApiFactory factory) : IClassFixture<OperationsApiFactory>
{
    [Fact]
    public async Task Flight_reads_and_scheduling_require_authentication()
    {
        using var client = factory.CreateClient();

        (await client.GetAsync($"{OperationsApiFactory.Base}/flights"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"{OperationsApiFactory.Base}/flights/{Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"{OperationsApiFactory.Base}/flights/customer-logos/{Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync($"{OperationsApiFactory.Base}/flights", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync($"{OperationsApiFactory.Base}/flights/bulk", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        foreach (var path in new[] { "flights/notification-targets", "flights/mobile/notification-targets" })
            (await client.PostAsJsonAsync($"{OperationsApiFactory.Base}/{path}", new { flightIds = new[] { Guid.NewGuid() } }))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Notification_target_checks_limit_request_size_and_allow_an_empty_batch()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        foreach (var path in new[] { "flights/notification-targets", "flights/mobile/notification-targets" })
        {
            var tooMany = await admin.PostAsJsonAsync($"{OperationsApiFactory.Base}/{path}",
                new { flightIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray() });
            tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await tooMany.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString()
                .ShouldBe("Operations.Flight.NotificationTargetsLimit");
        }
        var empty = await admin.PostAsJsonAsync($"{OperationsApiFactory.Base}/flights/notification-targets", new { flightIds = Array.Empty<Guid>() });
        empty.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await empty.Content.ReadFromJsonAsync<Guid[]>()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deferred_operation_endpoints_are_not_mapped()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var flightId = Guid.NewGuid();

        foreach (var path in new[]
        {
            $"{OperationsApiFactory.Base}/work-orders",
            $"{OperationsApiFactory.Base}/flights/{flightId}/operation",
            $"{OperationsApiFactory.Base}/flights/{flightId}/timeline",
            $"{OperationsApiFactory.Base}/dashboard",
            "/api/v1/mobile/flights"
        })
        {
            (await admin.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }
    }
}
