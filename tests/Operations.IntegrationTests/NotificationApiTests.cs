using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
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
        (await client.PostAsJsonAsync("/api/v1/notifications/me/read-inbox", new
        {
            throughCreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/v1/notifications/me/read-category", new
        {
            category = NotificationCategories.Invitations,
            throughCreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
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

    [Fact]
    public async Task Inbox_categories_filter_counts_pages_and_unread_without_exposing_foreign_or_archived_rows()
    {
        using var client = await factory.CreateAuthenticatedAdminClientAsync();
        var me = (await client.GetFromJsonAsync<MeDto>($"{OperationsApiFactory.IdentityBase}/me"))!;
        var now = DateTimeOffset.UtcNow;
        var invitation = CategoryItem(me.Id, NotificationKind.EmployeeInvitedToFlight, now.AddMinutes(7));
        var readInvitation = CategoryItem(me.Id, NotificationKind.EmployeeInvitedToFlight, now.AddMinutes(6), read: true);
        var reminder = CategoryItem(me.Id, NotificationKind.FlightReminder, now.AddMinutes(5));
        var readReminder = CategoryItem(me.Id, NotificationKind.FlightReminder, now.AddMinutes(4), read: true);
        var assignment = CategoryItem(me.Id, NotificationKind.StaffAssignedToFlight, now.AddMinutes(3));
        var schedule = CategoryItem(me.Id, NotificationKind.FlightScheduleUpdated, now.AddMinutes(2));
        var legacy = CategoryItem(me.Id, "LegacyFlightMessage", now.AddMinutes(1));
        var rows = new List<Notification> { invitation, readInvitation, reminder, readReminder, assignment, schedule, legacy };
        foreach (var kind in new[] { NotificationKind.EmployeeInvitedToFlight, NotificationKind.FlightReminder, NotificationKind.StaffAssignedToFlight })
        {
            rows.Add(CategoryItem(me.Id, kind, now.AddMinutes(20), archived: true));
            rows.Add(CategoryItem(Guid.NewGuid(), kind, now.AddMinutes(30)));
        }
        var ids = rows.Select(item => item.Id).ToArray();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.Notifications.AddRange(rows);
            await db.SaveChangesAsync();
        }
        try
        {
            var categories = new[]
            {
                (Category: NotificationCategories.Invitations, Total: 2L, Unread: 1L, FirstId: invitation.Id, SecondId: readInvitation.Id),
                (Category: NotificationCategories.Reminders, Total: 2L, Unread: 1L, FirstId: reminder.Id, SecondId: readReminder.Id),
                (Category: NotificationCategories.FlightUpdates, Total: 3L, Unread: 3L, FirstId: assignment.Id, SecondId: schedule.Id)
            };
            foreach (var category in categories)
            {
                var first = await client.GetFromJsonAsync<CategoryPageDto>(
                    $"/api/v1/notifications/me?category={category.Category}&page=1&pageSize=1");
                var second = await client.GetFromJsonAsync<CategoryPageDto>(
                    $"/api/v1/notifications/me?category={category.Category}&page=2&pageSize=1");
                var unread = await client.GetFromJsonAsync<CategoryPageDto>(
                    $"/api/v1/notifications/me?category={category.Category}&unreadOnly=true&pageSize=20");
                first.ShouldNotBeNull().TotalCount.ShouldBe(category.Total);
                first.Items.ShouldHaveSingleItem().Id.ShouldBe(category.FirstId);
                second.ShouldNotBeNull().TotalCount.ShouldBe(category.Total);
                second.Items.ShouldHaveSingleItem().Id.ShouldBe(category.SecondId);
                unread.ShouldNotBeNull().TotalCount.ShouldBe(category.Unread);
                unread.Items.ShouldAllBe(item => !item.IsRead);
            }
            var lastUpdate = await client.GetFromJsonAsync<CategoryPageDto>(
                "/api/v1/notifications/me?category=FlightUpdates&page=3&pageSize=1");
            lastUpdate.ShouldNotBeNull().Items.ShouldHaveSingleItem().Id.ShouldBe(legacy.Id);
            var all = await client.GetFromJsonAsync<CategoryPageDto>("/api/v1/notifications/me?pageSize=20");
            all.ShouldNotBeNull().TotalCount.ShouldBe(7);
            all.Items.Count.ShouldBe(7);
            var counts = (await client.GetFromJsonAsync<UnreadNotificationCountDto>("/api/v1/notifications/me/unread-count"))!;
            counts.Count.ShouldBe(5);
            counts.Invitations.ShouldBe(1);
            counts.Reminders.ShouldBe(1);
            counts.FlightUpdates.ShouldBe(3);
        }
        finally
        {
            await using var cleanup = factory.Services.CreateAsyncScope();
            var db = cleanup.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            await db.Notifications.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Unread_category_counts_cover_unloaded_pages_and_read_receipts_return_fresh_totals()
    {
        using var client = await factory.CreateAuthenticatedAdminClientAsync();
        var me = (await client.GetFromJsonAsync<MeDto>($"{OperationsApiFactory.IdentityBase}/me"))!;
        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        var rows = Enumerable.Range(0, 65)
            .Select(index => CategoryItem(me.Id, NotificationKind.EmployeeInvitedToFlight, cutoff.AddMinutes(-index)))
            .ToList();
        rows.AddRange(Enumerable.Range(0, 3)
            .Select(index => CategoryItem(me.Id, NotificationKind.FlightReminder, cutoff.AddMinutes(-index))));
        foreach (var kind in new[] { NotificationKind.StaffAssignedToFlight, NotificationKind.FlightScheduleUpdated, "LegacyFlightMessage" })
            rows.Add(CategoryItem(me.Id, kind, cutoff));
        foreach (var kind in new[] { NotificationKind.EmployeeInvitedToFlight, NotificationKind.FlightReminder, "LegacyFlightMessage" })
        {
            rows.Add(CategoryItem(me.Id, kind, cutoff, read: true));
            rows.Add(CategoryItem(me.Id, kind, cutoff, archived: true));
            rows.Add(CategoryItem(Guid.NewGuid(), kind, cutoff));
        }
        var ids = rows.Select(item => item.Id).ToArray();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.Notifications.AddRange(rows);
            await db.SaveChangesAsync();
        }

        try
        {
            var page = (await client.GetFromJsonAsync<CategoryPageDto>(
                "/api/v1/notifications/me?category=Invitations&page=1&pageSize=20"))!;
            page.Items.Count.ShouldBe(20);
            page.TotalCount.ShouldBe(66); // The unfiltered tab also includes its previously read row.
            var counts = (await client.GetFromJsonAsync<UnreadNotificationCountDto>("/api/v1/notifications/me/unread-count"))!;
            counts.ShouldBe(new UnreadNotificationCountDto(71, 65, 3, 3));

            var expected = new[]
            {
                (Category: NotificationCategories.Invitations, Counts: new UnreadNotificationCountDto(6, 0, 3, 3)),
                (Category: NotificationCategories.Reminders, Counts: new UnreadNotificationCountDto(3, 0, 0, 3)),
                (Category: NotificationCategories.FlightUpdates, Counts: new UnreadNotificationCountDto(0, 0, 0, 0))
            };
            foreach (var step in expected)
            {
                var response = await client.PostAsJsonAsync("/api/v1/notifications/me/read-category",
                    new { category = step.Category, throughCreatedAtUtc = cutoff });
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                var receipt = (await response.Content.ReadFromJsonAsync<NotificationCategoryReadDto>())!;
                new UnreadNotificationCountDto(receipt.UnreadCount, receipt.Invitations, receipt.Reminders, receipt.FlightUpdates)
                    .ShouldBe(step.Counts);
                (await client.GetFromJsonAsync<UnreadNotificationCountDto>("/api/v1/notifications/me/unread-count"))
                    .ShouldBe(step.Counts);
            }
        }
        finally
        {
            await using var cleanup = factory.Services.CreateAsyncScope();
            var db = cleanup.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            await db.Notifications.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Inbox_rejects_invalid_categories_with_the_standard_validation_problem()
    {
        using var client = await factory.CreateAuthenticatedAdminClientAsync();
        foreach (var category in new[] { "All", "invitations", "" })
        {
            var response = await client.GetAsync($"/api/v1/notifications/me?category={category}");
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("code").GetString().ShouldBe("Notifications.Category.Invalid");
        }
    }

    [Theory]
    [InlineData(NotificationCategories.Invitations, NotificationKind.EmployeeInvitedToFlight)]
    [InlineData(NotificationCategories.Reminders, NotificationKind.FlightReminder)]
    [InlineData(NotificationCategories.FlightUpdates, NotificationKind.StaffAssignedToFlight)]
    public async Task Category_read_uses_an_inclusive_cutoff_keeps_other_scopes_and_preserves_first_read_timestamps(
        string category, string kind)
    {
        using var client = await factory.CreateAuthenticatedAdminClientAsync();
        var me = (await client.GetFromJsonAsync<MeDto>($"{OperationsApiFactory.IdentityBase}/me"))!;
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-10);
        var old = CategoryItem(me.Id, kind, cutoff.AddMinutes(-1));
        var boundaryKind = category == NotificationCategories.FlightUpdates ? NotificationKind.FlightScheduleUpdated : kind;
        var boundary = CategoryItem(me.Id, boundaryKind, cutoff);
        var newer = CategoryItem(me.Id, kind, cutoff.AddMinutes(1));
        var previouslyRead = CategoryItem(me.Id, kind, cutoff.AddMinutes(-2), read: true);
        var previousReadAt = previouslyRead.ReadAtUtc;
        var archived = CategoryItem(me.Id, kind, cutoff.AddMinutes(-3), archived: true);
        var foreign = CategoryItem(Guid.NewGuid(), kind, cutoff.AddMinutes(-4));
        var rows = new List<Notification> { old, boundary, newer, previouslyRead, archived, foreign };
        if (category == NotificationCategories.FlightUpdates)
            rows.Add(CategoryItem(me.Id, "LegacyFlightMessage", cutoff.AddMinutes(-5)));
        foreach (var otherKind in new[] { NotificationKind.EmployeeInvitedToFlight, NotificationKind.FlightReminder, NotificationKind.StaffAssignedToFlight }
            .Where(candidate => candidate != kind))
            rows.Add(CategoryItem(me.Id, otherKind, cutoff.AddMinutes(-6)));
        var eligibleIds = rows.Where(item => item.RecipientUserId == me.Id && item.ReadAtUtc == null && item.ArchivedAtUtc == null
                && item.CreatedAtUtc <= cutoff && (category == NotificationCategories.FlightUpdates
                    ? item.Kind != NotificationKind.EmployeeInvitedToFlight && item.Kind != NotificationKind.FlightReminder
                    : item.Kind == kind))
            .Select(item => item.Id).ToArray();
        var ids = rows.Select(item => item.Id).ToList();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.Notifications.AddRange(rows);
            await db.SaveChangesAsync();
        }
        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/notifications/me/read-category", new
            {
                category,
                throughCreatedAtUtc = cutoff.ToOffset(TimeSpan.FromHours(3))
            });
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var receipt = (await response.Content.ReadFromJsonAsync<NotificationCategoryReadDto>())!;
            receipt.ReadAtUtc.Offset.ShouldBe(TimeSpan.Zero);
            receipt.ReadAtUtc.ShouldBeGreaterThan(cutoff);
            receipt.UnreadCount.ShouldBe(3);
            receipt.Invitations.ShouldBe(1);
            receipt.Reminders.ShouldBe(1);
            receipt.FlightUpdates.ShouldBe(1);
            await using (var verify = factory.Services.CreateAsyncScope())
            {
                var db = verify.ServiceProvider.GetRequiredService<NotificationsDbContext>();
                var persisted = await db.Notifications.AsNoTracking().Where(item => ids.Contains(item.Id)).ToListAsync();
                persisted.Where(item => eligibleIds.Contains(item.Id)).ShouldAllBe(item => item.ReadAtUtc == receipt.ReadAtUtc);
                persisted.Single(item => item.Id == previouslyRead.Id).ReadAtUtc.ShouldBe(previousReadAt);
                persisted.Where(item => !eligibleIds.Contains(item.Id) && item.Id != previouslyRead.Id)
                    .ShouldAllBe(item => item.ReadAtUtc == null);
            }

            var late = CategoryItem(me.Id, kind, cutoff.AddMinutes(2));
            ids.Add(late.Id);
            await using (var add = factory.Services.CreateAsyncScope())
            {
                var db = add.ServiceProvider.GetRequiredService<NotificationsDbContext>();
                db.Notifications.Add(late);
                await db.SaveChangesAsync();
            }
            var retry = await client.PostAsJsonAsync("/api/v1/notifications/me/read-category", new { category, throughCreatedAtUtc = cutoff });
            retry.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await retry.Content.ReadFromJsonAsync<NotificationCategoryReadDto>())!.UnreadCount.ShouldBe(4);
            await using var afterRetry = factory.Services.CreateAsyncScope();
            var current = afterRetry.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            (await current.Notifications.AsNoTracking().Where(item => eligibleIds.Contains(item.Id)).ToListAsync())
                .ShouldAllBe(item => item.ReadAtUtc == receipt.ReadAtUtc);
            (await current.Notifications.AsNoTracking().SingleAsync(item => item.Id == late.Id)).ReadAtUtc.ShouldBeNull();
        }
        finally
        {
            await using var cleanup = factory.Services.CreateAsyncScope();
            var db = cleanup.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            await db.Notifications.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Inbox_read_covers_all_kinds_through_cutoff_preserves_ownership_and_is_idempotent()
    {
        using var client = await factory.CreateAuthenticatedAdminClientAsync();
        var me = (await client.GetFromJsonAsync<MeDto>($"{OperationsApiFactory.IdentityBase}/me"))!;
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-10);
        var kinds = new[]
        {
            NotificationKind.EmployeeInvitedToFlight, NotificationKind.FlightReminder,
            NotificationKind.StaffAssignedToFlight, NotificationKind.FlightScheduleUpdated, "LegacyFlightMessage"
        };
        var eligible = kinds.Select((kind, index) => CategoryItem(me.Id, kind, cutoff.AddMinutes(-index))).ToList();
        var newer = CategoryItem(me.Id, NotificationKind.FlightReminder, cutoff.AddMinutes(1));
        var previouslyRead = CategoryItem(me.Id, NotificationKind.EmployeeInvitedToFlight, cutoff.AddMinutes(-2), read: true);
        var previousReadAt = previouslyRead.ReadAtUtc;
        var archived = CategoryItem(me.Id, NotificationKind.StaffAssignedToFlight, cutoff.AddMinutes(-3), archived: true);
        var foreign = CategoryItem(Guid.NewGuid(), NotificationKind.FlightScheduleUpdated, cutoff.AddMinutes(-4));
        var rows = eligible.Concat([newer, previouslyRead, archived, foreign]).ToList();
        var ids = rows.Select(item => item.Id).ToList();
        var eligibleIds = eligible.Select(item => item.Id).ToArray();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.Notifications.AddRange(rows);
            await db.SaveChangesAsync();
        }
        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/notifications/me/read-inbox", new
            {
                throughCreatedAtUtc = cutoff.ToOffset(TimeSpan.FromHours(3))
            });
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var receipt = (await response.Content.ReadFromJsonAsync<NotificationCategoryReadDto>())!;
            receipt.ReadAtUtc.Offset.ShouldBe(TimeSpan.Zero);
            receipt.ReadAtUtc.ShouldBeGreaterThan(cutoff);
            new UnreadNotificationCountDto(receipt.UnreadCount, receipt.Invitations, receipt.Reminders, receipt.FlightUpdates)
                .ShouldBe(new UnreadNotificationCountDto(1, 0, 1, 0));

            await using (var verify = factory.Services.CreateAsyncScope())
            {
                var db = verify.ServiceProvider.GetRequiredService<NotificationsDbContext>();
                var persisted = await db.Notifications.AsNoTracking().Where(item => ids.Contains(item.Id)).ToListAsync();
                persisted.Where(item => eligibleIds.Contains(item.Id)).ShouldAllBe(item => item.ReadAtUtc == receipt.ReadAtUtc);
                persisted.Single(item => item.Id == previouslyRead.Id).ReadAtUtc.ShouldBe(previousReadAt);
                persisted.Where(item => item.Id == newer.Id || item.Id == archived.Id || item.Id == foreign.Id)
                    .ShouldAllBe(item => item.ReadAtUtc == null);
            }

            var late = CategoryItem(me.Id, NotificationKind.StaffAssignedToFlight, cutoff.AddMinutes(2));
            ids.Add(late.Id);
            await using (var add = factory.Services.CreateAsyncScope())
            {
                var db = add.ServiceProvider.GetRequiredService<NotificationsDbContext>();
                db.Notifications.Add(late);
                await db.SaveChangesAsync();
            }
            var retry = await client.PostAsJsonAsync("/api/v1/notifications/me/read-inbox", new { throughCreatedAtUtc = cutoff });
            retry.StatusCode.ShouldBe(HttpStatusCode.OK);
            var retriedReceipt = (await retry.Content.ReadFromJsonAsync<NotificationCategoryReadDto>())!;
            new UnreadNotificationCountDto(retriedReceipt.UnreadCount, retriedReceipt.Invitations,
                retriedReceipt.Reminders, retriedReceipt.FlightUpdates).ShouldBe(new UnreadNotificationCountDto(2, 0, 1, 1));
            await using var afterRetry = factory.Services.CreateAsyncScope();
            var current = afterRetry.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            (await current.Notifications.AsNoTracking().Where(item => eligibleIds.Contains(item.Id)).ToListAsync())
                .ShouldAllBe(item => item.ReadAtUtc == receipt.ReadAtUtc);
            (await current.Notifications.AsNoTracking().SingleAsync(item => item.Id == late.Id)).ReadAtUtc.ShouldBeNull();
        }
        finally
        {
            await using var cleanup = factory.Services.CreateAsyncScope();
            var db = cleanup.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            await db.Notifications.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Inbox_read_rejects_missing_default_and_future_cutoffs()
    {
        using var client = await factory.CreateAuthenticatedAdminClientAsync();
        var cases = new[]
        {
            (Cutoff: (DateTimeOffset?)null, Code: "Notifications.CategoryRead.CutoffRequired"),
            (Cutoff: (DateTimeOffset?)default(DateTimeOffset), Code: "Notifications.CategoryRead.CutoffRequired"),
            (Cutoff: (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(1), Code: "Notifications.CategoryRead.CutoffInFuture")
        };
        foreach (var input in cases)
        {
            var response = await client.PostAsJsonAsync("/api/v1/notifications/me/read-inbox",
                new { throughCreatedAtUtc = input.Cutoff });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe(input.Code);
        }
    }

    [Fact]
    public async Task Category_read_rejects_invalid_categories_and_missing_default_or_future_cutoffs()
    {
        using var client = await factory.CreateAuthenticatedAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        var cases = new[]
        {
            (Category: (string?)"All", Cutoff: (DateTimeOffset?)now.AddMinutes(-1), Code: "Notifications.Category.Invalid"),
            (Category: (string?)null, Cutoff: (DateTimeOffset?)now.AddMinutes(-1), Code: "Notifications.Category.Invalid"),
            (Category: (string?)NotificationCategories.Invitations, Cutoff: (DateTimeOffset?)null, Code: "Notifications.CategoryRead.CutoffRequired"),
            (Category: (string?)NotificationCategories.Invitations, Cutoff: (DateTimeOffset?)default(DateTimeOffset), Code: "Notifications.CategoryRead.CutoffRequired"),
            (Category: (string?)NotificationCategories.Invitations, Cutoff: (DateTimeOffset?)now.AddDays(1), Code: "Notifications.CategoryRead.CutoffInFuture")
        };
        foreach (var input in cases)
        {
            var response = await client.PostAsJsonAsync("/api/v1/notifications/me/read-category",
                new { category = input.Category, throughCreatedAtUtc = input.Cutoff });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe(input.Code);
        }
    }

    private static Notification CategoryItem(Guid userId, string kind, DateTimeOffset createdAt, bool read = false, bool archived = false)
    {
        var item = Notification.Create(Guid.NewGuid(), userId, kind, "Notification", "Notification details",
            "إشعار", "تفاصيل الإشعار", "{}", createdAt).Value;
        if (read)
            item.MarkAsRead(createdAt);
        if (archived)
            item.Archive(createdAt);
        return item;
    }

    private sealed record CategoryItemDto(Guid Id, string Kind, bool IsRead);
    private sealed record CategoryPageDto(List<CategoryItemDto> Items, long TotalCount);
    private sealed record MeDto(Guid Id);
    private sealed record CountDto(int Count);
    private sealed record ItemDto(Guid Id, string TitleAr, Dictionary<string, string> Payload);
    private sealed record PageDto(List<ItemDto> Items, long TotalCount);
}
