using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Application.Features.Users;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Identity.Infrastructure.Notifications;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Identity.Infrastructure.UnitTests.Users;

public sealed class WorkOrderEmailPreferenceTests
{
    [Fact]
    public async Task New_accounts_are_opted_out_and_preference_round_trips_for_the_authenticated_user_only()
    {
        await using var db = CreateDb();
        var user = await AddUserAsync(db);
        var otherUser = await AddUserAsync(db);
        var stamp = user.SecurityStamp;
        var handler = new UpdateWorkOrderEmailPreferenceCommandHandler(db, new CurrentUser(user.Id), TimeProvider.System);
        var reader = new WorkOrderEmailRecipientReader(db);

        (await reader.GetAsync(user.Id))!.ReceiveWorkOrderSubmissionEmails.ShouldBeFalse();
        (await handler.Handle(new(true), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();

        var recipient = await reader.GetAsync(user.Id);
        recipient.ShouldNotBeNull();
        recipient.ReceiveWorkOrderSubmissionEmails.ShouldBeTrue();
        recipient.Email.ShouldBe(user.Email.Value);
        recipient.DisplayName.ShouldBe(user.DisplayName);
        (await reader.GetAsync(otherUser.Id))!.ReceiveWorkOrderSubmissionEmails.ShouldBeFalse();
        (await db.Users.SingleAsync(u => u.Id == user.Id)).SecurityStamp.ShouldBe(stamp);

        var me = await new GetCurrentUserQueryHandler(db, new CurrentUser(user.Id),
            new PermissionRegistry([new IdentityPermissionCatalog()]))
            .Handle(new(), CancellationToken.None);
        me.IsSuccess.ShouldBeTrue();
        me.Value.ReceiveWorkOrderSubmissionEmails.ShouldBeTrue();

        (await handler.Handle(new(false), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();
        (await reader.GetAsync(user.Id))!.ReceiveWorkOrderSubmissionEmails.ShouldBeFalse();
    }

    [Fact]
    public async Task Unauthenticated_request_cannot_change_a_users_preference()
    {
        await using var db = CreateDb();
        var user = await AddUserAsync(db);
        var handler = new UpdateWorkOrderEmailPreferenceCommandHandler(db, new CurrentUser(user.Id, false), TimeProvider.System);

        var result = await handler.Handle(new(true), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Unauthorized);
        (await new WorkOrderEmailRecipientReader(db).GetAsync(user.Id))!.ReceiveWorkOrderSubmissionEmails.ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_account_cannot_update_preferences_or_resolve_a_recipient()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var handler = new UpdateWorkOrderEmailPreferenceCommandHandler(db, new CurrentUser(userId), TimeProvider.System);

        var result = await handler.Handle(new(true), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Unauthorized);
        (await new WorkOrderEmailRecipientReader(db).GetAsync(userId)).ShouldBeNull();
    }

    [Theory]
    [InlineData(UserStatus.Invited)]
    [InlineData(UserStatus.Suspended)]
    [InlineData(UserStatus.Deactivated)]
    public async Task Inactive_accounts_cannot_opt_in_or_receive_work_orders(UserStatus status)
    {
        await using var db = CreateDb();
        var user = await AddUserAsync(db);
        db.Entry(user).Property(u => u.Status).CurrentValue = status;
        await db.SaveChangesAsync();
        var handler = new UpdateWorkOrderEmailPreferenceCommandHandler(db, new CurrentUser(user.Id), TimeProvider.System);

        var result = await handler.Handle(new(true), CancellationToken.None);

        result.Error.Code.ShouldBe("Identity.User.NotActive");
        user.ReceiveWorkOrderSubmissionEmails.ShouldBeFalse();
        (await new WorkOrderEmailRecipientReader(db).GetAsync(user.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Recipient_uses_verified_email_while_email_change_is_pending()
    {
        await using var db = CreateDb();
        var user = await AddUserAsync(db);
        user.SetWorkOrderEmailPreference(true, DateTimeOffset.UtcNow);
        user.RequestEmailChange(Email.Create("new-address@example.com").Value, "token", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var recipient = await new WorkOrderEmailRecipientReader(db).GetAsync(user.Id);

        recipient!.Email.ShouldBe(user.Email.Value);
        recipient.Email.ShouldNotBe(user.PendingEmail);
    }

    private static IdentityDbContext CreateDb() => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseInMemoryDatabase($"work-order-email-preference-{Guid.NewGuid():N}").Options);

    private static async Task<User> AddUserAsync(IdentityDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var role = Role.Create($"Staff-{Guid.NewGuid():N}", null, [], UserType.StationStaff, now).Value;
        var user = User.Invite(Email.Create($"staff-{Guid.NewGuid():N}@example.com").Value,
            "Station Staff", role.Id, "invitation-token", now.AddHours(1), now, UserType.StationStaff, Guid.NewGuid()).Value;
        user.Activate("invitation-token", "password-hash", now).IsSuccess.ShouldBeTrue();
        db.Roles.Add(role);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private sealed class CurrentUser(Guid? userId, bool authenticated = true) : ICurrentUser
    {
        public Guid? UserId => userId;
        public bool IsAuthenticated => authenticated;
    }
}
