using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.Notifications;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Notifications;

public sealed class NotificationPresentationTests
{
    [Fact]
    public async Task Presentation_uses_active_locale_and_builds_flight_deep_link()
    {
        var locale = new LocaleState(new NoOpJsRuntime());
        var flightId = Guid.NewGuid();
        var notification = Notification(flightId);

        NotificationPresentation.Title(notification, locale).ShouldBe("Assigned");
        NotificationPresentation.Body(notification, locale).ShouldBe("A teammate added you.");

        await locale.SetLanguageAsync(LocaleState.Arabic);
        try
        {
            NotificationPresentation.Title(notification, locale).ShouldBe("تم تعيينك");
            NotificationPresentation.Body(notification, locale).ShouldBe("أضافك أحد الزملاء.");
            NotificationPresentation.DeepLink(notification).ShouldBe($"/operations/flights/{flightId}");
        }
        finally
        {
            await locale.SetLanguageAsync(LocaleState.English);
        }
    }

    [Fact]
    public void DeepLink_is_null_for_missing_or_invalid_flight_id()
    {
        var missing = Notification(Guid.NewGuid()) with { Payload = new Dictionary<string, string>() };
        var invalid = Notification(Guid.NewGuid()) with { Payload = new Dictionary<string, string> { ["flightId"] = "not-a-guid" } };

        NotificationPresentation.DeepLink(missing).ShouldBeNull();
        NotificationPresentation.DeepLink(invalid).ShouldBeNull();
    }

    private static NotificationDto Notification(Guid flightId) => new(
        Guid.NewGuid(),
        "StaffAssignedToFlight",
        "Assigned",
        "A teammate added you.",
        "تم تعيينك",
        "أضافك أحد الزملاء.",
        new Dictionary<string, string> { ["FLIGHTID"] = flightId.ToString() },
        false,
        DateTimeOffset.UtcNow,
        null);

    private sealed class NoOpJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
    }
}
