using Identity.Domain.Sessions;
using Shouldly;

namespace Identity.Domain.UnitTests.Sessions;

public class UserSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SecurityStamp = Guid.Parse("42c19160-4c9b-46de-a77b-5f693699ea97");

    [Fact]
    public void Issue_creates_an_active_session()
    {
        var result = UserSession.Issue(
            Guid.NewGuid(), SecurityStamp, "hash", Now.AddDays(7), Now, "127.0.0.1", "agent");

        result.IsSuccess.ShouldBeTrue();
        var session = result.Value;
        session.IsActive(Now).ShouldBeTrue();
        session.FamilyId.ShouldBe(session.Id);
        session.SecurityStamp.ShouldBe(SecurityStamp);
        session.RevokedAtUtc.ShouldBeNull();
        session.CreatedByIp.ShouldBe("127.0.0.1");
    }

    [Fact]
    public void Issue_with_empty_user_fails()
    {
        var result = UserSession.Issue(Guid.Empty, SecurityStamp, "hash", Now.AddDays(7), Now);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Issue_with_empty_security_stamp_fails()
    {
        var result = UserSession.Issue(Guid.NewGuid(), Guid.Empty, "hash", Now.AddDays(7), Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Identity.Session.SecurityStampRequired");
    }

    [Fact]
    public void Issue_with_past_expiry_fails()
    {
        var result = UserSession.Issue(Guid.NewGuid(), SecurityStamp, "hash", Now.AddMinutes(-1), Now);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Revoke_marks_session_inactive()
    {
        var session = UserSession.Issue(
            Guid.NewGuid(), SecurityStamp, "hash", Now.AddDays(7), Now).Value;

        session.Revoke(Now.AddHours(1));

        session.RevokedAtUtc.ShouldNotBeNull();
        session.IsActive(Now.AddHours(2)).ShouldBeFalse();
    }

    [Fact]
    public void Revoke_is_idempotent_and_keeps_first_timestamp()
    {
        var session = UserSession.Issue(
            Guid.NewGuid(), SecurityStamp, "hash", Now.AddDays(7), Now).Value;
        var firstRevoke = Now.AddHours(1);

        session.Revoke(firstRevoke);
        session.Revoke(Now.AddHours(5));

        session.RevokedAtUtc.ShouldBe(firstRevoke);
    }

    [Fact]
    public void Revoke_never_precedes_session_creation()
    {
        var session = UserSession.Issue(
            Guid.NewGuid(), SecurityStamp, "hash", Now.AddDays(7), Now).Value;

        session.Revoke(Now.AddMinutes(-1));

        session.RevokedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void Expired_session_is_not_active()
    {
        var session = UserSession.Issue(
            Guid.NewGuid(), SecurityStamp, "hash", Now.AddHours(1), Now).Value;

        session.IsActive(Now.AddHours(2)).ShouldBeFalse();
    }

    [Fact]
    public void Refresh_successor_keeps_the_session_family()
    {
        var session = UserSession.Issue(
            Guid.NewGuid(), SecurityStamp, "hash-1", Now.AddDays(7), Now).Value;

        var successor = session.ContinueWith(
            "hash-2",
            Now.AddDays(8),
            Now.AddHours(1)).Value;

        successor.Id.ShouldNotBe(session.Id);
        successor.FamilyId.ShouldBe(session.FamilyId);
        successor.SecurityStamp.ShouldBe(SecurityStamp);
    }

    [Fact]
    public void Rebind_security_stamp_changes_only_the_session_generation()
    {
        var session = UserSession.Issue(
            Guid.NewGuid(), SecurityStamp, "hash", Now.AddDays(7), Now).Value;
        var replacement = Guid.NewGuid();

        session.RebindSecurityStamp(replacement).IsSuccess.ShouldBeTrue();

        session.SecurityStamp.ShouldBe(replacement);
        session.FamilyId.ShouldBe(session.Id);
    }
}
