using Identity.Domain.Sessions;
using Shouldly;

namespace Identity.Domain.UnitTests.Sessions;

public class UserSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issue_creates_an_active_session()
    {
        var result = UserSession.Issue(Guid.NewGuid(), "hash", Now.AddDays(7), Now, "127.0.0.1", "agent");

        result.IsSuccess.ShouldBeTrue();
        var session = result.Value;
        session.IsActive(Now).ShouldBeTrue();
        session.RevokedAtUtc.ShouldBeNull();
        session.CreatedByIp.ShouldBe("127.0.0.1");
    }

    [Fact]
    public void Issue_with_empty_user_fails()
    {
        var result = UserSession.Issue(Guid.Empty, "hash", Now.AddDays(7), Now);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Issue_with_past_expiry_fails()
    {
        var result = UserSession.Issue(Guid.NewGuid(), "hash", Now.AddMinutes(-1), Now);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Revoke_marks_session_inactive()
    {
        var session = UserSession.Issue(Guid.NewGuid(), "hash", Now.AddDays(7), Now).Value;

        session.Revoke(Now.AddHours(1));

        session.RevokedAtUtc.ShouldNotBeNull();
        session.IsActive(Now.AddHours(2)).ShouldBeFalse();
    }

    [Fact]
    public void Revoke_is_idempotent_and_keeps_first_timestamp()
    {
        var session = UserSession.Issue(Guid.NewGuid(), "hash", Now.AddDays(7), Now).Value;
        var firstRevoke = Now.AddHours(1);

        session.Revoke(firstRevoke);
        session.Revoke(Now.AddHours(5));

        session.RevokedAtUtc.ShouldBe(firstRevoke);
    }

    [Fact]
    public void Expired_session_is_not_active()
    {
        var session = UserSession.Issue(Guid.NewGuid(), "hash", Now.AddHours(1), Now).Value;

        session.IsActive(Now.AddHours(2)).ShouldBeFalse();
    }
}
