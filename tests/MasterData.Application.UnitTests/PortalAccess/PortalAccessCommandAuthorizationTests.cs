using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Authorization;
using MasterData.Application.Features.PortalAccess;
using MasterData.Domain.Authorization;
using Shouldly;

namespace MasterData.Application.UnitTests.PortalAccess;

public sealed class PortalAccessCommandAuthorizationTests
{
    [Fact]
    public async Task Grant_staff_portal_access_requires_specific_grant_permission()
    {
        var handler = new GrantStaffPortalAccessCommandHandler(
            db: null!,
            scope: AdminScope(),
            userContext: AdminWithoutGrantPermission(),
            timeProvider: TimeProvider.System);

        var result = await handler.Handle(
            new GrantStaffPortalAccessCommand(Guid.NewGuid(), Guid.NewGuid(), [1]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.PortalAccess.Forbidden");
    }

    [Fact]
    public async Task Grant_contact_portal_access_requires_specific_grant_permission()
    {
        var handler = new GrantContactPortalAccessCommandHandler(
            db: null!,
            scope: AdminScope(),
            userContext: AdminWithoutGrantPermission(),
            timeProvider: TimeProvider.System);

        var result = await handler.Handle(
            new GrantContactPortalAccessCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [1]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.PortalAccess.Forbidden");
    }

    [Fact]
    public async Task Grant_staff_portal_access_requires_administrator_scope()
    {
        var handler = new GrantStaffPortalAccessCommandHandler(
            db: null!,
            scope: ScopedStationStaffScope(),
            userContext: StationStaffWithGrantPermission(),
            timeProvider: TimeProvider.System);

        var result = await handler.Handle(
            new GrantStaffPortalAccessCommand(Guid.NewGuid(), Guid.NewGuid(), [1]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.PortalAccess.Forbidden");
    }

    [Fact]
    public async Task Grant_contact_portal_access_requires_administrator_scope()
    {
        var handler = new GrantContactPortalAccessCommandHandler(
            db: null!,
            scope: ScopedCustomerContactScope(),
            userContext: CustomerContactWithGrantPermission(),
            timeProvider: TimeProvider.System);

        var result = await handler.Handle(
            new GrantContactPortalAccessCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [1]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.PortalAccess.Forbidden");
    }

    [Fact]
    public async Task Grant_staff_portal_access_is_denied_by_write_scope_for_ViewerOnly_even_with_a_forged_claim()
    {
        var handler = new GrantStaffPortalAccessCommandHandler(
            db: null!,
            scope: new StaticMasterDataScope(
                new MasterDataScopeContext(UserType.ViewerOnly, null, null)),
            userContext: new TestUserContext(
                UserType.ViewerOnly,
                new HashSet<string>(StringComparer.Ordinal)
                {
                    MasterDataPermissions.StaffMembers.GrantAccess
                }),
            timeProvider: TimeProvider.System);

        var result = await handler.Handle(
            new GrantStaffPortalAccessCommand(Guid.NewGuid(), Guid.NewGuid(), [1]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.Scope.ReadOnly");
    }

    private static IMasterDataScope AdminScope() =>
        new StaticMasterDataScope(new MasterDataScopeContext(UserType.SystemAdministrator, null, null));

    private static IMasterDataScope ScopedStationStaffScope() =>
        new StaticMasterDataScope(new MasterDataScopeContext(UserType.StationStaff, Guid.NewGuid(), null));

    private static IMasterDataScope ScopedCustomerContactScope() =>
        new StaticMasterDataScope(new MasterDataScopeContext(UserType.CustomerContact, null, Guid.NewGuid()));

    private static IUserContext AdminWithoutGrantPermission() =>
        new TestUserContext(UserType.SystemAdministrator, new HashSet<string>(StringComparer.Ordinal)
        {
            MasterDataPermissions.StaffMembers.View
        });

    private static IUserContext StationStaffWithGrantPermission() =>
        new TestUserContext(UserType.StationStaff, new HashSet<string>(StringComparer.Ordinal)
        {
            MasterDataPermissions.StaffMembers.GrantAccess
        });

    private static IUserContext CustomerContactWithGrantPermission() =>
        new TestUserContext(UserType.CustomerContact, new HashSet<string>(StringComparer.Ordinal)
        {
            MasterDataPermissions.CustomerContacts.GrantAccess
        });

    private sealed class StaticMasterDataScope(MasterDataScopeContext context) : IMasterDataScope
    {
        public Task<Result<MasterDataScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Result<MasterDataScopeContext>>(context);
    }

    private sealed class TestUserContext(UserType userType, IReadOnlySet<string> permissions) : IUserContext
    {
        public bool IsAuthenticated => true;

        public Guid? UserId { get; } = Guid.NewGuid();

        public UserType? UserType => userType;

        public Guid? ExternalReferenceId => null;

        public bool HasPermission(string permission) => permissions.Contains(permission);
    }
}
