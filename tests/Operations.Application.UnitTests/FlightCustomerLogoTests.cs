using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Features.Flights;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightCustomerLogoTests
{
    [Theory]
    [InlineData("administrator", true)]
    [InlineData("viewer", true)]
    [InlineData("assigned", true)]
    [InlineData("per-landing", true)]
    [InlineData("dispatcher", true)]
    [InlineData("unassigned", false)]
    [InlineData("other-station", false)]
    public async Task LogoRead_UsesTheFlightVisibilityBoundaryBeforeReadingMasterData(string visibility, bool allowed)
    {
        await using var db = NewDb();
        var customerId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        db.Flights.Add(NewFlight(customerId, stationId,
            visibility is "assigned" or "other-station" ? staffId : null, visibility == "per-landing"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var context = visibility switch
        {
            "administrator" => new OperationsScopeContext(UserType.SystemAdministrator, null, null),
            "viewer" => new OperationsScopeContext(UserType.ViewerOnly, null, null),
            _ => new OperationsScopeContext(UserType.StationStaff,
                visibility == "other-station" ? Guid.NewGuid() : stationId, staffId,
                CanViewStationWide: visibility == "dispatcher")
        };
        var logos = new LogoReader();

        var result = await new GetFlightCustomerLogoQueryHandler(db, new StaticScope(context), logos)
            .Handle(new GetFlightCustomerLogoQuery(customerId), CancellationToken.None);

        result.IsSuccess.ShouldBe(allowed);
        logos.Calls.ShouldBe(allowed ? 1 : 0);
        if (allowed)
        {
            logos.CustomerId.ShouldBe(customerId);
            result.Value.Content.ShouldBe(logos.Content!.Content);
            result.Value.ContentType.ShouldBe("image/png");
        }
        else
        {
            result.Error.Type.ShouldBe(ErrorType.NotFound);
        }
    }

    [Fact]
    public async Task VisibleFlightForAnotherCustomer_DoesNotGrantLogoAccess()
    {
        await using var db = NewDb();
        db.Flights.Add(NewFlight(Guid.NewGuid(), Guid.NewGuid()));
        await db.SaveChangesAsync();
        var logos = new LogoReader();

        var result = await new GetFlightCustomerLogoQueryHandler(db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)), logos)
            .Handle(new GetFlightCustomerLogoQuery(Guid.NewGuid()), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.NotFound);
        logos.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task MissingOwnerLogo_ReturnsNotFound()
    {
        await using var db = NewDb();
        var customerId = Guid.NewGuid();
        db.Flights.Add(NewFlight(customerId, Guid.NewGuid()));
        await db.SaveChangesAsync();
        var logos = new LogoReader { Content = null };

        var result = await new GetFlightCustomerLogoQueryHandler(db,
            new StaticScope(new OperationsScopeContext(UserType.ViewerOnly, null, null)), logos)
            .Handle(new GetFlightCustomerLogoQuery(customerId), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.NotFound);
        logos.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task ScopeResolutionFailure_DoesNotReadLogos()
    {
        await using var db = NewDb();
        var logos = new LogoReader();
        var result = await new GetFlightCustomerLogoQueryHandler(db, new DeniedScope(), logos)
            .Handle(new GetFlightCustomerLogoQuery(Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.ShouldBe("Operations.Scope.Denied");
        logos.Calls.ShouldBe(0);
    }

    private static OperationsDbContext NewDb() => new(new DbContextOptionsBuilder<OperationsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Flight NewFlight(Guid customerId, Guid stationId, Guid? assignedStaffId = null, bool perLanding = false) =>
        Flight.ScheduleNew(new CustomerSnapshot(customerId, "SV", "Saudia"),
            new StationSnapshot(stationId, "RUH", "Station"), new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("123").Value,
            ScheduledTime.Create(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero)).Value,
            null, [new ServiceSnapshot(perLanding ? WellKnownMasterDataIds.AircraftPerLandingService : Guid.NewGuid(), "Service")],
            assignedStaffId is { } staffId ? [new StaffMemberSnapshot(staffId, "Assigned staff", "E1")] : [],
            Guid.NewGuid(), DateTimeOffset.UtcNow).Value;

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }

    private sealed class DeniedScope : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Failure<OperationsScopeContext>(Error.Forbidden("Scope denied.", "Operations.Scope.Denied")));
    }

    private sealed class LogoReader : ICustomerLogoReader
    {
        public CustomerLogoReadContent? Content { get; init; } = new([137, 80, 78, 71], "image/png");
        public int Calls { get; private set; }
        public Guid? CustomerId { get; private set; }

        public Task<CustomerLogoReadContent?> GetLogoAsync(Guid customerId, CancellationToken cancellationToken)
        {
            Calls++;
            CustomerId = customerId;
            return Task.FromResult(Content);
        }
    }
}
