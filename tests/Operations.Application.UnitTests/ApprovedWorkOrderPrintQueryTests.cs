using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Authorization;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class ApprovedWorkOrderPrintQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_ProjectsApprovedCompletionWithFullDetailAndOptionalSignature()
    {
        await using var db = NewDb();
        var ownerUserId = Guid.NewGuid();
        var flight = CreateFlight(contractNumber: "CTR-2017", perLanding: true);
        var workOrder = CreateCompletionWorkOrder(flight, ownerUserId, approve: false, signatureReference: "signatures/customer.png");
        workOrder.AddTaskAttachment(
            workOrder.Tasks.Single().Id,
            TaskAttachmentKind.Document,
            "attachments/report.pdf",
            "report.pdf",
            "application/pdf",
            100,
            Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        workOrder.Approve(17, "AMM-0017", Guid.NewGuid(), Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        flight.OnWorkOrderSubmitted(Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        flight.SettleCompleted(Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        byte[] signature = [0x89, 0x50, 0x4E, 0x47];
        var storage = new TestFileStorage(signature);
        var performedBy = workOrder.ServiceLines.Single().PerformedBy;
        var manpowerTypeId = Guid.NewGuid();
        var masterData = new TestMasterDataReader(
            [new StaffMemberReadSnapshot(
                performedBy.StaffMemberId,
                "Current catalog name",
                performedBy.EmployeeId,
                flight.Station.StationId,
                manpowerTypeId,
                IsActive: false)],
            [new ManpowerTypeReadSnapshot(manpowerTypeId, "Technician", IsActive: false)]);
        var handler = new GetApprovedWorkOrderPrintQueryHandler(
            db,
            OwnerScope(flight, ownerUserId),
            new TestUserContext(ownerUserId),
            storage,
            masterData);

        var result = await handler.Handle(new GetApprovedWorkOrderPrintQuery(flight.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AircraftManufacturer.ShouldBe("Airbus");
        result.Value.ContractNumber.ShouldBe("CTR-2017");
        result.Value.CustomerSignatureContent.ShouldBe(signature);
        result.Value.CustomerSignatureContentType.ShouldBe("image/png");
        storage.OpenedStorageKey.ShouldBe("signatures/customer.png");
        result.Value.WorkOrder.Id.ShouldBe(workOrder.Id);
        result.Value.WorkOrder.Type.ShouldBe(nameof(WorkOrderType.Completion));
        result.Value.WorkOrder.Status.ShouldBe(nameof(WorkOrderStatus.Approved));
        result.Value.WorkOrder.ServiceLines.ShouldHaveSingleItem().ServiceName.ShouldBe("Deicing");
        var task = result.Value.WorkOrder.Tasks.ShouldHaveSingleItem();
        task.Employees.ShouldHaveSingleItem().FullName.ShouldBe("Ramp Agent");
        task.Tools.ShouldHaveSingleItem().Name.ShouldBe("Towbar");
        task.Materials.ShouldHaveSingleItem().Name.ShouldBe("Hydraulic fluid");
        task.GeneralSupports.ShouldHaveSingleItem().Name.ShouldBe("GPU");
        task.Attachments.ShouldHaveSingleItem().OriginalFileName.ShouldBe("report.pdf");
        var staff = result.Value.Staff.ShouldHaveSingleItem();
        staff.StaffMemberId.ShouldBe(performedBy.StaffMemberId);
        staff.ManpowerTypeName.ShouldBe("Technician");
        masterData.RequestedStaffIds.ShouldBe([performedBy.StaffMemberId]);
        masterData.RequestedManpowerTypeIds.ShouldBe([manpowerTypeId]);

        var missingSignatureResult = await new GetApprovedWorkOrderPrintQueryHandler(
                db,
                OwnerScope(flight, ownerUserId),
                new TestUserContext(ownerUserId),
                new TestFileStorage(content: null),
                new TestMasterDataReader())
            .Handle(new GetApprovedWorkOrderPrintQuery(flight.Id), CancellationToken.None);

        missingSignatureResult.IsSuccess.ShouldBeTrue();
        missingSignatureResult.Value.CustomerSignatureContent.ShouldBeNull();
        missingSignatureResult.Value.CustomerSignatureContentType.ShouldBeNull();
        missingSignatureResult.Value.Staff.ShouldHaveSingleItem().ManpowerTypeName.ShouldBeNull();

        var transferredMasterData = new TestMasterDataReader(
            [new StaffMemberReadSnapshot(
                performedBy.StaffMemberId,
                performedBy.FullName,
                performedBy.EmployeeId,
                Guid.NewGuid(),
                manpowerTypeId,
                IsActive: true)],
            [new ManpowerTypeReadSnapshot(manpowerTypeId, "Transferred role", IsActive: true)]);
        var transferredResult = await new GetApprovedWorkOrderPrintQueryHandler(
                db,
                OwnerScope(flight, ownerUserId),
                new TestUserContext(ownerUserId),
                new TestFileStorage(content: null),
                transferredMasterData)
            .Handle(new GetApprovedWorkOrderPrintQuery(flight.Id), CancellationToken.None);

        transferredResult.IsSuccess.ShouldBeTrue();
        transferredResult.Value.Staff.ShouldHaveSingleItem().ManpowerTypeName.ShouldBeNull();
        transferredMasterData.RequestedManpowerTypeIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsNotFoundWhenFlightHasNoApprovedCompletion()
    {
        await using var db = NewDb();
        var flight = CreateFlight();
        var submittedCompletion = CreateCompletionWorkOrder(flight, Guid.NewGuid(), approve: false);
        var approvedCancellation = CreateCancellationWorkOrder(flight, Guid.NewGuid());
        db.Flights.Add(flight);
        db.WorkOrders.AddRange(submittedCompletion, approvedCancellation);
        await db.SaveChangesAsync();

        var result = await AdministratorHandler(db).Handle(
            new GetApprovedWorkOrderPrintQuery(flight.Id),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.ApprovedCompletionNotFound");
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_ReturnsNotFoundWhenApprovedCompletionFlightIsNotCompleted()
    {
        await using var db = NewDb();
        var flight = CreateFlight();
        var workOrder = CreateCompletionWorkOrder(flight, Guid.NewGuid(), approve: false);
        workOrder.Approve(19, "AMM-0019", Guid.NewGuid(), Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();

        var result = await AdministratorHandler(db).Handle(
            new GetApprovedWorkOrderPrintQuery(flight.Id),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.ApprovedCompletionNotFound");
    }

    [Fact]
    public async Task Handle_UsesOwnerAndViewOthersVisibilityWithoutWideningStationScope()
    {
        await using var db = NewDb();
        var ownerUserId = Guid.NewGuid();
        var callerUserId = Guid.NewGuid();
        var flight = CreateFlight();
        var workOrder = CreateCompletionWorkOrder(flight, ownerUserId, approve: true);
        db.Flights.Add(flight);
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();

        var caller = new TestUserContext(callerUserId);
        var denied = await new GetApprovedWorkOrderPrintQueryHandler(
                db,
                new StaticScope(new OperationsScopeContext(
                    UserType.StationStaff,
                    flight.Station.StationId,
                    Guid.NewGuid(),
                    CanViewWorkOrdersStationWide: false)),
                caller,
                new TestFileStorage(null),
                new TestMasterDataReader())
            .Handle(new GetApprovedWorkOrderPrintQuery(flight.Id), CancellationToken.None);

        denied.IsFailure.ShouldBeTrue();
        denied.Error.Code.ShouldBe("Operations.WorkOrder.ApprovedCompletionNotFound");

        var stationWide = await new GetApprovedWorkOrderPrintQueryHandler(
                db,
                new StaticScope(new OperationsScopeContext(
                    UserType.StationStaff,
                    flight.Station.StationId,
                    Guid.NewGuid(),
                    CanViewWorkOrdersStationWide: true)),
                caller,
                new TestFileStorage(null),
                new TestMasterDataReader())
            .Handle(new GetApprovedWorkOrderPrintQuery(flight.Id), CancellationToken.None);

        stationWide.IsSuccess.ShouldBeTrue();
        stationWide.Value.WorkOrder.Id.ShouldBe(workOrder.Id);

        var administrator = await AdministratorHandler(db).Handle(
            new GetApprovedWorkOrderPrintQuery(flight.Id),
            CancellationToken.None);

        administrator.IsSuccess.ShouldBeTrue();
        administrator.Value.WorkOrder.Id.ShouldBe(workOrder.Id);

        var otherStation = await new GetApprovedWorkOrderPrintQueryHandler(
                db,
                new StaticScope(new OperationsScopeContext(
                    UserType.StationStaff,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    CanViewWorkOrdersStationWide: true)),
                caller,
                new TestFileStorage(null),
                new TestMasterDataReader())
            .Handle(new GetApprovedWorkOrderPrintQuery(flight.Id), CancellationToken.None);

        otherStation.IsFailure.ShouldBeTrue();
        otherStation.Error.Code.ShouldBe("Operations.WorkOrder.ApprovedCompletionNotFound");
    }

    private static OperationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase($"approved-work-order-print-{Guid.NewGuid()}")
            .Options);

    private static GetApprovedWorkOrderPrintQueryHandler AdministratorHandler(OperationsDbContext db) =>
        new(
            db,
            new StaticScope(new OperationsScopeContext(UserType.SystemAdministrator, null, null)),
            new TestUserContext(Guid.NewGuid(), UserType.SystemAdministrator),
            new TestFileStorage(null),
            new TestMasterDataReader());

    private static IOperationsScope OwnerScope(Flight flight, Guid userId) =>
        new StaticScope(new OperationsScopeContext(
            UserType.StationStaff,
            flight.Station.StationId,
            Guid.NewGuid(),
            UserId: userId));

    private static Flight CreateFlight(string? contractNumber = null, bool perLanding = false)
    {
        IReadOnlyList<ServiceSnapshot> plannedServices = perLanding
            ? [new ServiceSnapshot(WellKnownMasterDataIds.AircraftPerLandingService, "Aircraft Per Landing")]
            : [new ServiceSnapshot(Guid.NewGuid(), "Ground handling")];

        return Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "RJ", "Royal Jordanian"),
            new StationSnapshot(Guid.NewGuid(), "AMM", "Amman"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Turnaround"),
            FlightNumber.Create("RJ123").Value,
            ScheduledTime.Create(Now, Now.AddHours(2)).Value,
            new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320"),
            plannedServices,
            assignedEmployees: [],
            contractId: contractNumber is null ? null : Guid.NewGuid(),
            contractNumber,
            createdByUserId: Guid.NewGuid(),
            now: Now).Value;
    }

    private static WorkOrder CreateCompletionWorkOrder(
        Flight flight,
        Guid ownerUserId,
        bool approve,
        string? signatureReference = null)
    {
        var employee = new StaffMemberSnapshot(Guid.NewGuid(), "Ramp Agent", "EMP-100");
        var workOrder = WorkOrder.SubmitNew(
            flight,
            WorkOrderType.Completion,
            ownerUserId,
            employee,
            FlightNumber.Create("RJ124").Value,
            new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320"),
            "JY-ABC",
            ActualTime.Create(Now.AddMinutes(5), Now.AddHours(1)).Value,
            cancellation: null,
            remarks: "Completed without delay",
            serviceLines:
            [
                new WorkOrderServiceLineInput(
                    new ServiceSnapshot(Guid.NewGuid(), "Deicing"),
                    employee,
                    TimeWindow.Create(Now, Now.AddMinutes(20)).Value,
                    "Deiced wings")
            ],
            tasks:
            [
                new WorkOrderTaskInput(
                    Id: null,
                    TaskType.Major,
                    "Turnaround support",
                    TimeWindow.Create(Now, Now.AddMinutes(30)).Value,
                    Employees: [employee],
                    Tools: [new WorkOrderTaskToolInput(new ToolSnapshot(Guid.NewGuid(), "Towbar"), Quantity.Create(1).Value)],
                    Materials: [new WorkOrderTaskMaterialInput(new MaterialSnapshot(Guid.NewGuid(), "Hydraulic fluid"), Quantity.Create(2).Value)],
                    GeneralSupports: [new WorkOrderTaskGeneralSupportInput(new GeneralSupportSnapshot(Guid.NewGuid(), "GPU"), Quantity.Create(1).Value)])
            ],
            Now).Value;

        if (signatureReference is not null)
        {
            workOrder.SetCustomerSignature(
                signatureReference,
                "customer.png",
                "image/png",
                4,
                Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        }

        if (approve)
        {
            workOrder.Approve(17, "AMM-0017", Guid.NewGuid(), Now.AddHours(2)).IsSuccess.ShouldBeTrue();
            flight.OnWorkOrderSubmitted(Now.AddHours(1)).IsSuccess.ShouldBeTrue();
            flight.SettleCompleted(Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        }

        return workOrder;
    }

    private static WorkOrder CreateCancellationWorkOrder(Flight flight, Guid ownerUserId)
    {
        var employee = new StaffMemberSnapshot(Guid.NewGuid(), "Ramp Agent", "EMP-200");
        var workOrder = WorkOrder.SubmitNew(
            flight,
            WorkOrderType.Cancellation,
            ownerUserId,
            employee,
            actualFlightNumber: null,
            aircraftType: null,
            aircraftTailNumber: null,
            actuals: null,
            CancellationDetails.Create(Now, "Customer canceled").Value,
            remarks: null,
            serviceLines: [],
            tasks: [],
            Now).Value;
        workOrder.Approve(18, "AMM-0018", Guid.NewGuid(), Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        return workOrder;
    }

    private sealed class StaticScope(OperationsScopeContext context) : IOperationsScope
    {
        public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(context));
    }

    private sealed class TestUserContext(Guid userId, UserType userType = UserType.StationStaff) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => userId;
        public UserType? UserType => userType;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => false;
    }

    private sealed class TestFileStorage(byte[]? content) : IFileStorage
    {
        public string? OpenedStorageKey { get; private set; }

        public Task<StoredFile> SaveAsync(
            string container,
            string fileName,
            string contentType,
            Stream content,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            OpenedStorageKey = storageKey;
            return Task.FromResult<Stream?>(content is null ? null : new MemoryStream(content, writable: false));
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestMasterDataReader(
        IReadOnlyList<StaffMemberReadSnapshot>? staff = null,
        IReadOnlyList<ManpowerTypeReadSnapshot>? manpowerTypes = null) : IMasterDataReader
    {
        private readonly IReadOnlyDictionary<Guid, StaffMemberReadSnapshot> staffById =
            (staff ?? []).ToDictionary(item => item.Id);
        private readonly IReadOnlyDictionary<Guid, ManpowerTypeReadSnapshot> manpowerTypesById =
            (manpowerTypes ?? []).ToDictionary(item => item.Id);

        public List<Guid> RequestedStaffIds { get; } = [];
        public List<Guid> RequestedManpowerTypeIds { get; } = [];

        public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetStaffMembersAsync(
            IReadOnlyCollection<Guid> ids,
            CancellationToken cancellationToken)
        {
            RequestedStaffIds.AddRange(ids);
            IReadOnlyList<StaffMemberReadSnapshot> result = ids
                .Where(staffById.ContainsKey)
                .Select(id => staffById[id])
                .ToList();
            return Task.FromResult(result);
        }

        public Task<ManpowerTypeReadSnapshot?> GetManpowerTypeAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            RequestedManpowerTypeIds.Add(id);
            return Task.FromResult(manpowerTypesById.GetValueOrDefault(id));
        }

        public Task<CustomerReadSnapshot?> GetCustomerAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<StationReadSnapshot?> GetStationAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<OperationTypeReadSnapshot?> GetOperationTypeAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AircraftTypeReadSnapshot?> GetAircraftTypeAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ServiceReadSnapshot?> GetServiceAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceReadSnapshot>> GetServicesAsync(
            IReadOnlyCollection<Guid> ids,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StaffMemberReadSnapshot?> GetStaffMemberAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetActiveStaffMembersForStationAsync(
            Guid stationId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ToolReadSnapshot?> GetToolAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<MaterialReadSnapshot?> GetMaterialAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<GeneralSupportReadSnapshot?> GetGeneralSupportAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlySet<Guid>> GetAllowedActiveServiceIdsAsync(
            Guid manpowerTypeId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceReadSnapshot>> GetActiveServicesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ToolReadSnapshot>> GetActiveToolsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<MaterialReadSnapshot>> GetActiveMaterialsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<GeneralSupportReadSnapshot>> GetActiveGeneralSupportsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<CustomerReadSnapshot>> GetActiveCustomersAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<AircraftTypeReadSnapshot>> GetActiveAircraftTypesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
