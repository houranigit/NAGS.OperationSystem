using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Notifications.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class NotificationPreferenceEndpointTests(OperationsApiFactory factory) : IClassFixture<OperationsApiFactory>
{
    private const string Path = "/api/v1/notifications/me/preferences";

    [Fact]
    public async Task Preferences_require_authentication_and_validate_both_request_fields()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync(Path)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PatchAsJsonAsync(Path, new { key = "enabled", enabled = false })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var client = await factory.CreateAuthenticatedAdminClientAsync();
        (await client.PatchAsJsonAsync(Path, new { key = "unknown", enabled = true })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PatchAsJsonAsync(Path, new { key = "enabled" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Single_choice_updates_are_durable_across_sessions_without_overwriting_other_choices()
    {
        using var client = await factory.CreateAuthenticatedAdminClientAsync();
        (await client.GetFromJsonAsync<NotificationPreferencesDto>(Path)).ShouldBe(new NotificationPreferencesDto());

        var assignments = client.PatchAsJsonAsync(Path, new { key = "flightAssignments", enabled = false });
        var schedules = client.PatchAsJsonAsync(Path, new { key = "scheduleChanges", enabled = false });
        var responses = await Task.WhenAll(assignments, schedules);
        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);

        using var secondSession = await factory.CreateAuthenticatedAdminClientAsync();
        (await secondSession.GetFromJsonAsync<NotificationPreferencesDto>(Path))
            .ShouldBe(new NotificationPreferencesDto(FlightAssignments: false, ScheduleChanges: false));

        var enabled = await secondSession.PatchAsJsonAsync(Path, new { key = "flightAssignments", enabled = true });
        (await enabled.Content.ReadFromJsonAsync<NotificationPreferencesDto>())
            .ShouldBe(new NotificationPreferencesDto(ScheduleChanges: false));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        (await db.NotificationPreferences.CountAsync()).ShouldBe(2);
    }
}
