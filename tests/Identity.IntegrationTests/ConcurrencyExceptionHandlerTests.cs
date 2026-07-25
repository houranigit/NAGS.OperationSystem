using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OperationsSystem.Api;
using Shouldly;

namespace Identity.IntegrationTests;

public sealed class ConcurrencyExceptionHandlerTests
{
    [Fact]
    public async Task DbUpdateConcurrencyException_maps_to_stable_409_problem_details()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();

        await using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            TraceIdentifier = "trace-concurrency-test"
        };
        context.Request.Path = "/identity/users/target";
        context.Response.Body = new MemoryStream();

        var handled = await new ConcurrencyExceptionHandler().TryHandleAsync(
            context,
            new DbUpdateConcurrencyException(),
            CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        context.Response.ContentType.ShouldStartWith("application/problem+json");

        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(
            context.Response.Body,
            cancellationToken: CancellationToken.None);
        payload.RootElement.GetProperty("title").GetString().ShouldBe("Conflict");
        payload.RootElement.GetProperty("status").GetInt32().ShouldBe(StatusCodes.Status409Conflict);
        payload.RootElement.GetProperty("detail").GetString().ShouldBe(
            "The record was changed by someone else. Reload it and try again.");
        payload.RootElement.GetProperty("code").GetString().ShouldBe("General.ConcurrencyConflict");
    }

    [Fact]
    public async Task Non_concurrency_exception_is_not_handled()
    {
        var context = new DefaultHttpContext();

        var handled = await new ConcurrencyExceptionHandler().TryHandleAsync(
            context,
            new InvalidOperationException(),
            CancellationToken.None);

        handled.ShouldBeFalse();
        context.Response.HasStarted.ShouldBeFalse();
    }
}
