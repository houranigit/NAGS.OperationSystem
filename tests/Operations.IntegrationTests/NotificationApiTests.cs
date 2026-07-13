using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Domain.Notifications;
using Notifications.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class NotificationApiTests(OperationsApiFactory factory) : IClassFixture<OperationsApiFactory>
{
    [Fact]
    public async Task Inbox_and_device_endpoints_require_authentication()
    {
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/v1/notifications/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/notifications/me/unread-count")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/v1/notifications/me/devices", new
        {
            token = "untrusted-fid",
            platform = "Android",
            deviceId = "untrusted-installation"
        })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Inbox_supports_unread_read_archive_and_device_registration()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var me = await client.GetFromJsonAsync<MeDto>($"{OperationsApiFactory.IdentityBase}/me");
        me.ShouldNotBeNull();

        var notificationId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.Notifications.Add(Notification.Create(
                notificationId,
                me!.Id,
                NotificationKind.StaffAssignedToFlight,
                "You were assigned to a flight",
                "A teammate added you to flight SV400.",
                "تم تعيينك في رحلة",
                "أضافك أحد زملائك إلى الرحلة SV400.",
                $"{{\"flightId\":\"{Guid.NewGuid()}\",\"flightNumber\":\"SV400\"}}",
                DateTimeOffset.UtcNow).Value);
            await db.SaveChangesAsync();
        }

        var count = await client.GetFromJsonAsync<CountDto>("/api/v1/notifications/me/unread-count");
        count!.Count.ShouldBe(1);

        var inbox = await client.GetFromJsonAsync<PageDto>("/api/v1/notifications/me?page=1&pageSize=20");
        inbox!.TotalCount.ShouldBe(1);
        inbox.Items.Single().TitleAr.ShouldBe("تم تعيينك في رحلة");
        inbox.Items.Single().Payload["flightNumber"].ShouldBe("SV400");

        (await client.PostAsync($"/api/v1/notifications/{notificationId}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        count = await client.GetFromJsonAsync<CountDto>("/api/v1/notifications/me/unread-count");
        count!.Count.ShouldBe(0);

        var register = await client.PostAsJsonAsync("/api/v1/notifications/me/devices", new
        {
            token = "integration-fcm-token",
            platform = "Android",
            deviceId = "integration-installation",
            locale = "ar",
            appVersion = "1.0.0"
        });
        register.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsync($"/api/v1/notifications/{notificationId}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        inbox = await client.GetFromJsonAsync<PageDto>("/api/v1/notifications/me?page=1&pageSize=20");
        inbox!.TotalCount.ShouldBe(0);

        var revoke = await client.PostAsJsonAsync("/api/v1/notifications/me/devices/revoke", new { token = "integration-fcm-token" });
        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        (await verifyDb.DeviceTokens.SingleAsync()).RevokedAtUtc.ShouldNotBeNull();
    }

    private sealed record MeDto(Guid Id);
    private sealed record CountDto(int Count);
    private sealed record ItemDto(Guid Id, string TitleAr, Dictionary<string, string> Payload);
    private sealed record PageDto(List<ItemDto> Items, long TotalCount);
}
