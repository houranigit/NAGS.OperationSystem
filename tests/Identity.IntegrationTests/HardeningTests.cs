using System.Net;
using Shouldly;

namespace Identity.IntegrationTests;

/// <summary>Phase 6 release-hardening coverage: readiness checks and correlation-id propagation.</summary>
public class HardeningTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    [Fact]
    public async Task Readiness_endpoint_reports_healthy_when_databases_are_reachable()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Liveness_endpoint_is_healthy()
    {
        var client = factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Responses_carry_a_correlation_id_header()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.Headers.Contains("X-Correlation-ID").ShouldBeTrue();
    }

    [Fact]
    public async Task A_supplied_correlation_id_is_echoed_back()
    {
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", "test-correlation-123");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-ID").ShouldContain("test-correlation-123");
    }
}
