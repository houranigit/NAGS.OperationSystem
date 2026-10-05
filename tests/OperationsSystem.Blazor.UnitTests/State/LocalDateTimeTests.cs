using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Shared;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.State;

public sealed class LocalDateTimeTests
{
    [Fact]
    public async Task Timestamp_waits_for_browser_zone_and_renders_local_date_with_zone()
    {
        var runtime = new PendingTimeZoneRuntime();
        var zone = new UserTimeZone(runtime);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(zone);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        var component = await renderer.Dispatcher.InvokeAsync(() =>
            renderer.BeginRenderingComponent<LocalDateTime>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(LocalDateTime.Value)] = DateTimeOffset.Parse("2026-10-05T01:55:00Z"),
                    [nameof(LocalDateTime.ShowZone)] = true
                })));

        zone.IsInitialized.ShouldBeFalse();
        var pendingHtml = await renderer.Dispatcher.InvokeAsync(component.ToHtmlString);
        pendingHtml.ShouldNotContain("<time");
        pendingHtml.ShouldNotContain("2026-10-05 01:55");

        runtime.Complete("America/Chicago", -300);
        await component.QuiescenceTask;

        zone.IsInitialized.ShouldBeTrue();
        var html = await renderer.Dispatcher.InvokeAsync(component.ToHtmlString);
        html.ShouldContain("2026-10-04 20:55 (America/Chicago)");
        html.ShouldContain("2026-10-04 20:55:00 (America/Chicago)");
        html.ShouldNotContain("2026-10-05 01:55");
    }

    private sealed class PendingTimeZoneRuntime : IJSRuntime
    {
        private readonly TaskCompletionSource<string> response = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(string id, int offsetMinutes) =>
            response.SetResult(JsonSerializer.Serialize(new { id, offsetMinutes }));

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.ShouldBe("operationsSystem.timeZone.get");
            var json = await response.Task.WaitAsync(cancellationToken);
            return JsonSerializer.Deserialize<TValue>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
    }
}
