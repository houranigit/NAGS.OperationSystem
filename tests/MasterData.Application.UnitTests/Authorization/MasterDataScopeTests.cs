using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Authorization;
using Shouldly;

namespace MasterData.Application.UnitTests.Authorization;

public sealed class MasterDataScopeTests
{
    [Fact]
    public async Task ViewerOnly_resolves_as_unlinked_global_reader()
    {
        var scope = new MasterDataScope(new TestUserContext(UserType.ViewerOnly), db: null!);

        var result = await scope.ResolveAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserType.ShouldBe(UserType.ViewerOnly);
        result.Value.StationId.ShouldBeNull();
        result.Value.CustomerId.ShouldBeNull();
        result.Value.HasGlobalReadAccess.ShouldBeTrue();
        result.Value.IsAdministrator.ShouldBeFalse();
        result.Value.EnsureStation(Guid.NewGuid()).IsSuccess.ShouldBeTrue();
        result.Value.EnsureCustomer(Guid.NewGuid()).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ViewerOnly_write_resolution_and_scoped_write_checks_are_denied()
    {
        var scope = new MasterDataScope(new TestUserContext(UserType.ViewerOnly), db: null!);

        var resolution = await scope.ResolveForWriteAsync(CancellationToken.None);
        var stationCheck = await scope.CheckStationForWriteAsync(Guid.NewGuid(), CancellationToken.None);
        var customerCheck = await scope.CheckCustomerForWriteAsync(Guid.NewGuid(), CancellationToken.None);

        resolution.IsFailure.ShouldBeTrue();
        resolution.Error.Code.ShouldBe("MasterData.Scope.ReadOnly");
        stationCheck.IsFailure.ShouldBeTrue();
        stationCheck.Error.Code.ShouldBe("MasterData.Scope.ReadOnly");
        customerCheck.IsFailure.ShouldBeTrue();
        customerCheck.Error.Code.ShouldBe("MasterData.Scope.ReadOnly");
    }

    [Fact]
    public async Task Existing_scoped_account_types_keep_boundary_limited_write_access()
    {
        var stationId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var stationScope = new StaticScope(
            new MasterDataScopeContext(UserType.StationStaff, stationId, null));
        var customerScope = new StaticScope(
            new MasterDataScopeContext(UserType.CustomerContact, null, customerId));

        (await stationScope.CheckStationForWriteAsync(stationId, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        (await stationScope.CheckStationForWriteAsync(Guid.NewGuid(), CancellationToken.None))
            .Error.Code.ShouldBe("MasterData.Scope.Forbidden");
        (await customerScope.CheckCustomerForWriteAsync(customerId, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        (await customerScope.CheckCustomerForWriteAsync(Guid.NewGuid(), CancellationToken.None))
            .Error.Code.ShouldBe("MasterData.Scope.Forbidden");
    }

    private sealed class TestUserContext(UserType userType) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public UserType? UserType => userType;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => false;
    }

    private sealed class StaticScope(MasterDataScopeContext context) : IMasterDataScope
    {
        public Task<Result<MasterDataScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }
}
