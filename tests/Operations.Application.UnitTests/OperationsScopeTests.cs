using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using Operations.Application.Authorization;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class OperationsScopeTests
{
    [Fact]
    public async Task ViewerOnly_resolves_as_unlinked_global_reader()
    {
        var scope = new OperationsScope(new TestUserContext(UserType.ViewerOnly), masterData: null!);

        var result = await scope.ResolveAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserType.ShouldBe(UserType.ViewerOnly);
        result.Value.StationId.ShouldBeNull();
        result.Value.StaffMemberId.ShouldBeNull();
        result.Value.HasGlobalReadAccess.ShouldBeTrue();
        result.Value.IsAdministrator.ShouldBeFalse();
        result.Value.EnsureStation(Guid.NewGuid()).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ViewerOnly_write_resolution_is_denied()
    {
        var scope = new OperationsScope(new TestUserContext(UserType.ViewerOnly), masterData: null!);

        var result = await scope.ResolveForWriteAsync(CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.Scope.ReadOnly");
    }

    [Fact]
    public async Task Existing_writable_account_types_keep_write_access()
    {
        var administrator = new StaticScope(
            new OperationsScopeContext(UserType.SystemAdministrator, null, null));
        var stationStaff = new StaticScope(
            new OperationsScopeContext(UserType.StationStaff, Guid.NewGuid(), Guid.NewGuid()));

        (await administrator.ResolveForWriteAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await stationStaff.ResolveForWriteAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    private sealed class TestUserContext(UserType userType) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public UserType? UserType => userType;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => false;
    }

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<BuildingBlocks.Domain.Results.Result<OperationsScopeContext>> ResolveAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(BuildingBlocks.Domain.Results.Result.Success(context));
    }
}
