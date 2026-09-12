using System.Text;
using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Contracts.Email;
using BuildingBlocks.Infrastructure.Email;
using Identity.Contracts;
using MasterData.Contracts.Readers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Operations.Application.Abstractions;
using Operations.Application.Contracts;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Domain.WorkOrders;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class WorkOrderSubmissionEmailQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_opted_out_recipient_never_renders_or_enqueues(bool missing)
    {
        await using var db = CreateDb();
        var flight = CreateFlight();
        var workOrder = CreateWorkOrder(flight);
        var renderer = new RecordingRenderer();
        var recipients = new RecipientReader(missing ? null : new(workOrder.OwnerUserId, "owner@example.test", "Owner", false));
        var queue = CreateQueue(db, recipients, renderer);

        var result = await queue.EnqueueAsync(workOrder, flight, workOrder.OwnerUserId, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        renderer.Sources.ShouldBeEmpty();
        db.OutboxMessages.Local.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(WorkOrderType.Completion)]
    [InlineData(WorkOrderType.Cancellation)]
    public async Task Opted_in_owner_gets_one_encrypted_pdf_snapshot_in_the_submission_save(WorkOrderType type)
    {
        await using var db = CreateDb();
        var flight = CreateFlight();
        var workOrder = CreateWorkOrder(flight, type);
        db.Flights.Add(flight);
        db.WorkOrders.Add(workOrder);
        var recipients = new RecipientReader(new(workOrder.OwnerUserId, "owner@example.test", "Owner <script>", true));
        var renderer = new RecordingRenderer();
        var protector = new DataProtectionEmailContentProtector(new EphemeralDataProtectionProvider());
        var queue = new WorkOrderSubmissionEmailQueue(db, recipients,
            new WorkOrderPrintSourceBuilder(new UnusedStorage(), new UnusedMasterDataReader()), renderer, protector,
            NullLogger<WorkOrderSubmissionEmailQueue>.Instance);

        var result = await queue.EnqueueAsync(workOrder, flight, workOrder.OwnerUserId, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        recipients.RequestedIds.ShouldBe([workOrder.OwnerUserId]);
        renderer.Sources.ShouldHaveSingleItem().WorkOrder.Status.ShouldBe("Submitted");
        renderer.Sources.Single().WorkOrder.Type.ShouldBe(type.ToString());
        (await db.OutboxMessages.CountAsync()).ShouldBe(0);
        db.OutboxMessages.Local.Count.ShouldBe(1);
        await db.SaveChangesAsync();
        (await db.WorkOrders.CountAsync()).ShouldBe(1);
        var outbox = await db.OutboxMessages.SingleAsync();
        var request = JsonSerializer.Deserialize<EmailDeliveryRequested>(outbox.Content)!;
        request.ToEmail.ShouldBe("owner@example.test");
        request.Kind.ShouldBe("work-order-submission");
        protector.Unprotect(request.ProtectedBody).ShouldContain("Owner &lt;script&gt;");
        var attachments = JsonSerializer.Deserialize<EmailAttachment[]>(protector.Unprotect(request.ProtectedAttachments!))!;
        attachments.ShouldHaveSingleItem().ContentType.ShouldBe("application/pdf");
        Encoding.UTF8.GetString(attachments[0].Content).ShouldContain("Original submitted remarks");

        workOrder.UpdateDetails(type, workOrder.ActualFlightNumber, null, null, null, workOrder.Cancellation,
            "Later change", [], [], Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        (await db.OutboxMessages.SingleAsync()).Content.ShouldBe(outbox.Content);
        Encoding.UTF8.GetString(attachments[0].Content).ShouldNotContain("Later change");
    }

    [Fact]
    public async Task Pdf_render_failure_does_not_persist_work_order_or_email()
    {
        await using var db = CreateDb();
        var flight = CreateFlight();
        var workOrder = CreateWorkOrder(flight);
        db.Flights.Add(flight);
        db.WorkOrders.Add(workOrder);
        var recipients = new RecipientReader(new(workOrder.OwnerUserId, "owner@example.test", "Owner", true));
        var queue = CreateQueue(db, recipients, new RecordingRenderer(fail: true));

        var result = await queue.EnqueueAsync(workOrder, flight, workOrder.OwnerUserId, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.SubmissionEmailFailed");
        db.OutboxMessages.Local.ShouldBeEmpty();
        (await db.OutboxMessages.CountAsync()).ShouldBe(0);
        (await db.WorkOrders.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Submit_handler_keeps_flight_and_work_order_unsaved_when_receipt_rendering_fails()
    {
        await using var db = CreateDb();
        var flight = CreateFlight();
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var ownerId = Guid.NewGuid();
        var user = new AdminUser(ownerId);
        var masterData = new UnusedMasterDataReader();
        var resolver = new MasterDataResolver(masterData);
        var queue = CreateQueue(db, new RecipientReader(new(ownerId, "owner@example.test", "Owner", true)), new RecordingRenderer(fail: true));
        var handler = new SubmitWorkOrderCommandHandler(db, new OperationsScope(user, masterData), new WorkOrderInputBuilder(resolver),
            resolver, new UnusedStorage(), new FlightTimelineWriter(db, user, masterData), new WorkOrderTimelineWriter(db, user, masterData),
            queue, new NoMobileSync(), user, TimeProvider.System);
        var payload = new WorkOrderEditableCommandPayload(null, null, null, null, null, Now, "Flight canceled", null, [], []);

        var result = await handler.Handle(new SubmitWorkOrderCommand(flight.Id, WorkOrderType.Cancellation, payload), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.SubmissionEmailFailed");
        (await db.WorkOrders.AsNoTracking().CountAsync()).ShouldBe(0);
        (await db.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(0);
        (await db.Flights.AsNoTracking().SingleAsync()).Status.ShouldBe(FlightStatus.Scheduled);
    }

    [Theory]
    [InlineData("SV\n101")]
    [InlineData("SV\r101")]
    [InlineData("SV\r\n101")]
    [InlineData("SV\0\t101")]
    public async Task Receipt_subject_normalizes_flight_number_controls_before_durable_enqueue(string flightNumber)
    {
        await using var db = CreateDb();
        var flight = CreateFlight();
        var workOrder = CreateWorkOrder(flight);
        var actualFlightNumber = FlightNumber.Create(flightNumber);
        actualFlightNumber.IsSuccess.ShouldBeTrue();
        workOrder.UpdateDetails(workOrder.Type, actualFlightNumber.Value, workOrder.AircraftType, null,
            workOrder.Actuals, null, workOrder.Remarks, [], [], Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        var queue = CreateQueue(db, new RecipientReader(new(workOrder.OwnerUserId, "owner@example.test", "Owner", true)), new RecordingRenderer());

        var result = await queue.EnqueueAsync(workOrder, flight, workOrder.OwnerUserId, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var message = JsonSerializer.Deserialize<EmailDeliveryRequested>(db.OutboxMessages.Local.Single().Content)!;
        message.Subject.Any(char.IsControl).ShouldBeFalse();
        message.Subject.ShouldStartWith("Work order submitted — SV");
        message.Subject.ShouldEndWith("101");
        using var mail = new System.Net.Mail.MailMessage { Subject = message.Subject };
        mail.Subject.ShouldBe(message.Subject);
        workOrder.ActualFlightNumber.Value.ShouldBe(flightNumber);
    }

    [Theory]
    [InlineData("other-user")]
    [InlineData("system")]
    [InlineData("approved")]
    [InlineData("returned")]
    public async Task Non_submission_paths_do_not_queue_receipts(string scenario)
    {
        await using var db = CreateDb();
        var flight = CreateFlight();
        var workOrder = CreateWorkOrder(flight);
        if (scenario is "approved" or "returned")
            workOrder.Approve(1, "RUH-0001", Guid.NewGuid(), Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        if (scenario == "returned")
            workOrder.Return(Guid.NewGuid(), "Revise", Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        var recipients = new RecipientReader(new(workOrder.OwnerUserId, "owner@example.test", "Owner", true));
        var renderer = new RecordingRenderer();
        var queue = CreateQueue(db, recipients, renderer);
        var actorId = scenario == "system" ? Guid.Empty : scenario == "other-user" ? Guid.NewGuid() : workOrder.OwnerUserId;

        var result = await queue.EnqueueAsync(workOrder, flight, actorId, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        recipients.RequestedIds.ShouldBeEmpty();
        renderer.Sources.ShouldBeEmpty();
        db.OutboxMessages.Local.ShouldBeEmpty();
    }

    private static OperationsDbContext CreateDb() => new(new DbContextOptionsBuilder<OperationsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static WorkOrderSubmissionEmailQueue CreateQueue(OperationsDbContext db, RecipientReader recipients, RecordingRenderer renderer) =>
        new(db, recipients, new WorkOrderPrintSourceBuilder(new UnusedStorage(), new UnusedMasterDataReader()), renderer,
            new DataProtectionEmailContentProtector(new EphemeralDataProtectionProvider()), NullLogger<WorkOrderSubmissionEmailQueue>.Instance);

    private static Flight CreateFlight() => Flight.ScheduleNew(
        new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"), new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh"),
        new OperationTypeSnapshot(Guid.NewGuid(), "Transit"), FlightNumber.Create("SV101").Value,
        ScheduledTime.Create(Now, Now.AddHours(1)).Value, null,
        [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")], [], null, null, Guid.NewGuid(), Now).Value;

    private static WorkOrder CreateWorkOrder(Flight flight, WorkOrderType type = WorkOrderType.Completion) =>
        WorkOrder.SubmitNew(flight, type, Guid.NewGuid(), null, null,
            type == WorkOrderType.Completion ? new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320") : null, null,
            type == WorkOrderType.Completion ? ActualTime.Create(Now, Now.AddHours(1)).Value : null,
            type == WorkOrderType.Cancellation ? CancellationDetails.Create(Now, "Flight canceled").Value : null,
            "Original submitted remarks", [], [], Now).Value;

    private sealed class RecipientReader(WorkOrderEmailRecipient? recipient) : IWorkOrderEmailRecipientReader
    {
        public List<Guid> RequestedIds { get; } = [];
        public Task<WorkOrderEmailRecipient?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            RequestedIds.Add(userId);
            return Task.FromResult(recipient);
        }
    }

    private sealed class RecordingRenderer(bool fail = false) : IWorkOrderPdfRenderer
    {
        public List<ApprovedWorkOrderPrintDto> Sources { get; } = [];
        public WorkOrderPdfFile Create(ApprovedWorkOrderPrintDto source)
        {
            if (fail)
                throw new IOException("PDF rendering failed");
            Sources.Add(source);
            return new(Encoding.UTF8.GetBytes($"%PDF fixture {source.WorkOrder.Remarks}"), $"WO-{source.WorkOrder.Id:D}.pdf");
        }
    }

    private sealed class UnusedStorage : IFileStorage
    {
        public Task<StoredFile> SaveAsync(string container, string fileName, string contentType, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class AdminUser(Guid userId) : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => userId;
        public UserType? UserType => BuildingBlocks.Contracts.Authorization.UserType.SystemAdministrator;
        public Guid? ExternalReferenceId => null;
        public bool HasPermission(string permission) => true;
    }

    private sealed class NoMobileSync : IMobileSyncBroadcaster
    {
        public void Enqueue(MobileSyncChange change) { }
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task BroadcastNowAsync(MobileSyncChange change, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class UnusedMasterDataReader : IMasterDataReader
    {
        public Task<CustomerReadSnapshot?> GetCustomerAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<StationReadSnapshot?> GetStationAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<OperationTypeReadSnapshot?> GetOperationTypeAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<AircraftTypeReadSnapshot?> GetAircraftTypeAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<ServiceReadSnapshot?> GetServiceAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceReadSnapshot>> GetServicesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<StaffMemberReadSnapshot?> GetStaffMemberAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetStaffMembersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<StaffMemberReadSnapshot>> GetActiveStaffMembersForStationAsync(Guid stationId, CancellationToken ct) => throw new NotSupportedException();
        public Task<ToolReadSnapshot?> GetToolAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<MaterialReadSnapshot?> GetMaterialAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<GeneralSupportReadSnapshot?> GetGeneralSupportAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<ManpowerTypeReadSnapshot?> GetManpowerTypeAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlySet<Guid>> GetAllowedActiveServiceIdsAsync(Guid manpowerTypeId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceReadSnapshot>> GetActiveServicesAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ToolReadSnapshot>> GetActiveToolsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<MaterialReadSnapshot>> GetActiveMaterialsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneralSupportReadSnapshot>> GetActiveGeneralSupportsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<CustomerReadSnapshot>> GetActiveCustomersAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AircraftTypeReadSnapshot>> GetActiveAircraftTypesAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
