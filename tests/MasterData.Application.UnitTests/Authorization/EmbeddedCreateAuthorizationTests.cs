using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using MasterData.Application.Features.Customers;
using MasterData.Application.Features.StaffMembers;
using MasterData.Application.Features.Stations;
using MasterData.Domain.Authorization;
using Shouldly;

namespace MasterData.Application.UnitTests.Authorization;

public sealed class EmbeddedCreateAuthorizationTests
{
    [Fact]
    public async Task Creating_a_station_with_staff_requires_staff_member_create_permission()
    {
        var handler = new CreateStationCommandHandler(
            db: null!,
            userContext: UserWithoutEmbeddedCreatePermissions(),
            timeProvider: TimeProvider.System);

        var result = await handler.Handle(
            new CreateStationCommand(
                "ORD",
                null,
                "Chicago",
                "Chicago",
                Guid.NewGuid(),
                [new NewStationStaffInput(
                    "Station employee",
                    "EMP-001",
                    "employee@example.com",
                    Guid.NewGuid(),
                    EmploymentContract: null,
                    WorkingDays: [],
                    Licenses: [],
                    PortalAccessRoleId: null)]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.StaffMember.CreateForbidden");
    }

    [Fact]
    public async Task Creating_a_customer_with_contacts_requires_customer_contact_create_permission()
    {
        var handler = new CreateCustomerCommandHandler(
            db: null!,
            userContext: UserWithoutEmbeddedCreatePermissions(),
            timeProvider: TimeProvider.System);

        var result = await handler.Handle(
            new CreateCustomerCommand(
                null,
                null,
                "Example customer",
                Guid.NewGuid(),
                null,
                null,
                new CustomerAddressInput(null, null, null, null, null),
                [new CustomerContactInput(
                    Id: null,
                    Name: "Customer contact",
                    JobTitle: null,
                    Email: "contact@example.com",
                    Phone: null,
                    PortalAccessRoleId: null)]),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.CustomerContact.CreateForbidden");
    }

    private static IUserContext UserWithoutEmbeddedCreatePermissions() =>
        new TestUserContext(new HashSet<string>(StringComparer.Ordinal)
        {
            MasterDataPermissions.Stations.Create,
            MasterDataPermissions.Customers.Create
        });

    private sealed class TestUserContext(IReadOnlySet<string> permissions) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }
}
