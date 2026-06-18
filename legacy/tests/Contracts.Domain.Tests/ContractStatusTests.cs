using Contracts.Domain.Enumerations;
using Contracts.Domain.Tests.Fixtures;
using Xunit;

namespace Contracts.Domain.Tests;

public sealed class ContractStatusTests
{
    private static readonly DateTimeOffset Start = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2031, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("AAAA0000-0000-0000-0000-000000000001");

    [Fact]
    public void SyncAutomaticStatus_transitions_draft_to_active_when_inside_period()
    {
        var contract = ContractFixture.BuildValidContract(
            start: Start, end: End, now: Start.AddMonths(-1));
        Assert.Equal(ContractStatus.Draft, contract.Status);

        contract.SyncAutomaticStatus(Start.AddMonths(2));

        Assert.Equal(ContractStatus.Active, contract.Status);
    }

    [Fact]
    public void SyncAutomaticStatus_transitions_active_to_expired_after_period()
    {
        var contract = ContractFixture.BuildValidContract(
            start: Start, end: End, now: Start.AddMonths(2));
        Assert.Equal(ContractStatus.Active, contract.Status);

        contract.SyncAutomaticStatus(End.AddDays(1));

        Assert.Equal(ContractStatus.Expired, contract.Status);
    }

    [Fact]
    public void SyncAutomaticStatus_does_not_change_terminated_contracts()
    {
        var contract = ContractFixture.BuildValidContract(
            start: Start, end: End, now: Start.AddMonths(2));
        contract.Terminate("Customer wished to end early.", Actor, Start.AddMonths(3));

        var sync = contract.SyncAutomaticStatus(End.AddYears(1));

        Assert.True(sync.IsSuccess);
        Assert.Equal(ContractStatus.Terminated, contract.Status);
    }

    [Fact]
    public void Suspend_then_Activate_resumes_to_period_appropriate_status()
    {
        var contract = ContractFixture.BuildValidContract(
            start: Start, end: End, now: Start.AddMonths(2));

        var suspend = contract.Suspend("Pending review", Actor, Start.AddMonths(3));
        Assert.True(suspend.IsSuccess);
        Assert.Equal(ContractStatus.Suspended, contract.Status);

        var activate = contract.Activate(Actor, Start.AddMonths(4));
        Assert.True(activate.IsSuccess);
        Assert.Equal(ContractStatus.Active, contract.Status);
    }

    [Fact]
    public void Suspend_from_non_active_fails()
    {
        var contract = ContractFixture.BuildValidContract(
            start: Start, end: End, now: Start.AddMonths(-1));
        Assert.Equal(ContractStatus.Draft, contract.Status);

        var suspend = contract.Suspend("nope", Actor, Start);

        Assert.True(suspend.IsFailure);
        Assert.Contains("Active contract", suspend.Error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Activate_from_non_suspended_fails()
    {
        var contract = ContractFixture.BuildValidContract(
            start: Start, end: End, now: Start.AddMonths(2));
        Assert.Equal(ContractStatus.Active, contract.Status);

        var activate = contract.Activate(Actor, Start.AddMonths(3));

        Assert.True(activate.IsFailure);
        Assert.Contains("Suspended contract", activate.Error.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Terminate_records_actor_reason_and_timestamp()
    {
        var contract = ContractFixture.BuildValidContract(
            start: Start, end: End, now: Start.AddMonths(2));
        var atUtc = Start.AddMonths(3);

        var result = contract.Terminate("Customer breach", Actor, atUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(ContractStatus.Terminated, contract.Status);
        Assert.NotNull(contract.Termination);
        Assert.Equal("Customer breach", contract.Termination!.Reason);
        Assert.Equal(Actor, contract.Termination.ByUserId);
        Assert.Equal(atUtc.UtcDateTime, contract.Termination.AtUtc);
    }

    [Fact]
    public void Terminate_already_terminated_fails()
    {
        var contract = ContractFixture.BuildValidContract(
            start: Start, end: End, now: Start.AddMonths(2));
        contract.Terminate("First", Actor, Start.AddMonths(3));

        var second = contract.Terminate("Second", Actor, Start.AddMonths(4));

        Assert.True(second.IsFailure);
    }

    [Fact]
    public void Terminate_after_expired_fails()
    {
        var contract = ContractFixture.BuildValidContract(
            start: Start, end: End, now: End.AddDays(1));
        Assert.Equal(ContractStatus.Expired, contract.Status);

        var terminate = contract.Terminate("late", Actor, End.AddDays(2));
        Assert.True(terminate.IsFailure);
    }
}
