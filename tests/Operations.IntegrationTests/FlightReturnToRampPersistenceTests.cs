using MasterData.Contracts.Resources;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;
using Testcontainers.MsSql;

namespace Operations.IntegrationTests;

public sealed class FlightReturnToRampPersistenceTests(FlightReturnToRampPersistenceTests.DatabaseFixture fixture)
    : IClassFixture<FlightReturnToRampPersistenceTests.DatabaseFixture>
{
    private const string TaskBaseline = "20261108204611_Operations_FlightTasks";
    private const string ReturnMigration = "20261109002742_Operations_FlightReturnToRamps";
    private const string SignatureMigration = "20261109153847_Operations_UnifyCustomerSignatures";
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Return_migration_preserves_existing_flight_work_submission_data_and_versions()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync(TaskBaseline);
        var previous = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var actor = Guid.NewGuid();
        var flight = DepartedFlight();
        var work = AddWork(flight, null, actor, Now.AddMinutes(1), Now.AddMinutes(-30));
        var canceled = ScheduleFlight();
        canceled.SubmitCancellation(Employee(), CancellationDetails.Create(Now, "Canceled").Value, actor, Now).IsSuccess.ShouldBeTrue();
        await PreRtrMigrationSeed.SaveAsync(db, flight, canceled);
        var version = flight.RowVersion.ToArray();
        var canceledVersion = canceled.RowVersion.ToArray();
        db.ChangeTracker.Clear();

        await db.Database.MigrateAsync(ReturnMigration);

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        applied.Length.ShouldBe(previous.Length + 1);
        applied.Take(previous.Length).ShouldBe(previous);
        applied.Last().ShouldContain("ReturnToRamp");
        await db.Database.MigrateAsync();
        var reloaded = await LoadAsync(db, flight.Id);
        reloaded.RowVersion.ShouldBe(version);
        reloaded.CreatedAtUtc.ShouldBe(flight.CreatedAtUtc);
        reloaded.UpdatedAtUtc.ShouldBe(flight.UpdatedAtUtc);
        reloaded.Customer.ShouldBe(flight.Customer);
        reloaded.Station.ShouldBe(flight.Station);
        reloaded.FlightNumber.ShouldBe(flight.FlightNumber);
        reloaded.Schedule.ShouldBe(flight.Schedule);
        reloaded.AircraftType.ShouldBe(flight.AircraftType);
        reloaded.PlannedServices.ShouldHaveSingleItem().Id.ShouldBe(flight.PlannedServices.Single().Id);
        reloaded.AssignedEmployees.ShouldHaveSingleItem().Id.ShouldBe(flight.AssignedEmployees.Single().Id);
        reloaded.DepartureSubmissions.ShouldHaveSingleItem().Details.ShouldBe(flight.DepartureSubmissions.Single().Details);
        reloaded.LastReturnToRampSequence.ShouldBe(0);
        reloaded.ReturnToRamps.ShouldBeEmpty();
        var savedService = reloaded.Services.ShouldHaveSingleItem();
        savedService.Id.ShouldBe(work.Service.Id);
        savedService.ReturnToRampId.ShouldBeNull();
        savedService.Performers.ShouldHaveSingleItem().Id.ShouldBe(work.Service.Performers.Single().Id);
        savedService.Attachments.ShouldHaveSingleItem().Details.ShouldBe(work.Service.Attachments.Single().Details);
        var savedTask = reloaded.Tasks.ShouldHaveSingleItem();
        savedTask.Id.ShouldBe(work.Task.Id);
        savedTask.ReturnToRampId.ShouldBeNull();
        savedTask.AtaChapter.ShouldBe(work.Task.AtaChapter);
        savedTask.Tools.ShouldHaveSingleItem().Usage.ShouldBe(work.Task.Tools.Single().Usage);
        savedTask.Materials.ShouldHaveSingleItem().Usage.ShouldBe(work.Task.Materials.Single().Usage);
        savedTask.GeneralSupports.ShouldHaveSingleItem().Usage.ShouldBe(work.Task.GeneralSupports.Single().Usage);
        savedTask.Performers.ShouldHaveSingleItem().Id.ShouldBe(work.Task.Performers.Single().Id);
        savedTask.Attachments.ShouldHaveSingleItem().Details.ShouldBe(work.Task.Attachments.Single().Details);
        var preservedCancellation = await LoadAsync(db, canceled.Id);
        preservedCancellation.RowVersion.ShouldBe(canceledVersion);
        preservedCancellation.CancellationSubmissions.ShouldHaveSingleItem().Details
            .ShouldBe(canceled.CancellationSubmissions.Single().Details);
        preservedCancellation.ReturnToRamps.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Occurrences_round_trip_shared_work_graphs_metadata_and_optional_signatures(bool signed)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var actor = Guid.NewGuid();
        var closer = Guid.NewGuid();
        var flight = DepartedFlight();
        // Neither of these two submissions is accepted; recorded departure is sufficient.
        flight.SubmitDeparture(Employee(), Departure(), actor, Now).IsSuccess.ShouldBeTrue();
        flight.DepartureSubmissions.ShouldAllBe(item => !item.IsAccepted);
        var normal = AddWork(flight, null, actor, Now, Now.AddMinutes(-30));
        var receiver = Employee();
        var arrival = RtrArrivalDetails.Create(Now.ToOffset(TimeSpan.FromHours(3)), receiver, "  Returned for inspection  ").Value;
        var first = flight.StartReturnToRamp(arrival, actor, Now.AddMinutes(1)).Value;
        var work = AddWork(flight, first.Id, actor, Now.AddMinutes(2), Now);
        var handler = Employee();
        var signature = signed ? Signature() : null;
        var departure = RtrDepartureDetails.Create(Now.AddMinutes(30).ToOffset(TimeSpan.FromHours(3)), signature,
            handler, "  Returned to service  ").Value;
        flight.CloseReturnToRamp(first.Id, departure, closer, Now.AddMinutes(31)).IsSuccess.ShouldBeTrue();
        var second = flight.StartReturnToRamp(Arrival(Now.AddMinutes(40)), actor, Now.AddMinutes(41)).Value;
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadAsync(db, flight.Id);
        reloaded.LastReturnToRampSequence.ShouldBe(2);
        reloaded.ReturnToRamps.Select(item => item.Sequence).OrderBy(sequence => sequence).ShouldBe([1, 2]);
        reloaded.Services.ShouldHaveSingleItem().Id.ShouldBe(normal.Service.Id);
        reloaded.Tasks.ShouldHaveSingleItem().Id.ShouldBe(normal.Task.Id);
        reloaded.ServiceRecords.Count.ShouldBe(2);
        reloaded.TaskRecords.Count.ShouldBe(2);
        var saved = reloaded.ReturnToRamps.Single(item => item.Id == first.Id);
        saved.FlightId.ShouldBe(flight.Id);
        saved.ArrivalDetails.ReceivedByEmployee.ShouldBe(receiver);
        saved.ArrivalDetails.Remarks.ShouldBe("Returned for inspection");
        saved.ArrivalDetails.ArrivedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        saved.DepartureDetails.ShouldNotBeNull().HandledByEmployee.ShouldBe(handler);
        saved.DepartureDetails.Remarks.ShouldBe("Returned to service");
        saved.DepartureDetails.DepartedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        saved.DepartureDetails.CustomerSignature.ShouldBe(signature);
        if (signed)
        {
            saved.DepartureDetails.CustomerSignature.ShouldNotBeNull().FileName.ShouldBe("signature.png");
            saved.DepartureDetails.CustomerSignature.StorageKey.ShouldBe("signatures/return.png");
            saved.DepartureDetails.CustomerSignature.SignedAtUtc.Offset.ShouldBe(TimeSpan.Zero);
        }
        saved.CreatedByUserId.ShouldBe(actor);
        saved.CreatedAtUtc.ShouldBe(Now.AddMinutes(1));
        saved.EndedByUserId.ShouldBe(closer);
        saved.UpdatedByUserId.ShouldBe(closer);
        saved.UpdatedAtUtc.ShouldBe(Now.AddMinutes(31));
        saved.IsOpen.ShouldBeFalse();
        saved.Window.ShouldBe(TimeWindow.Create(Now, Now.AddMinutes(30)).Value);
        var savedService = saved.Services.ShouldHaveSingleItem();
        savedService.Id.ShouldBe(work.Service.Id);
        savedService.ReturnToRampId.ShouldBe(saved.Id);
        savedService.ShouldBeSameAs(reloaded.ServiceRecords.Single(item => item.Id == work.Service.Id));
        savedService.Performers.ShouldHaveSingleItem().Id.ShouldBe(work.Service.Performers.Single().Id);
        savedService.Attachments.ShouldHaveSingleItem().Id.ShouldBe(work.Service.Attachments.Single().Id);
        var savedTask = saved.Tasks.ShouldHaveSingleItem();
        savedTask.Id.ShouldBe(work.Task.Id);
        savedTask.ReturnToRampId.ShouldBe(saved.Id);
        savedTask.ShouldBeSameAs(reloaded.TaskRecords.Single(item => item.Id == work.Task.Id));
        savedTask.AtaChapter.ShouldBe(work.Task.AtaChapter);
        savedTask.Materials.ShouldHaveSingleItem().Usage.ShouldBe(Quantity(12.25m));
        savedTask.Tools.ShouldHaveSingleItem().Usage.ShouldBe(work.Task.Tools.Single().Usage);
        savedTask.GeneralSupports.ShouldHaveSingleItem().Id.ShouldBe(work.Task.GeneralSupports.Single().Id);
        savedTask.Performers.ShouldHaveSingleItem().Id.ShouldBe(work.Task.Performers.Single().Id);
        savedTask.Attachments.ShouldHaveSingleItem().Id.ShouldBe(work.Task.Attachments.Single().Id);
        var open = reloaded.ReturnToRamps.Single(item => item.Id == second.Id);
        open.IsOpen.ShouldBeTrue();
        open.DepartureDetails.ShouldBeNull();
        open.Window.ToUtc.ShouldBeNull();
        open.Services.ShouldBeEmpty();
        open.Tasks.ShouldBeEmpty();

        var editedArrival = RtrArrivalDetails.Create(Now.AddMinutes(-1), receiver, "Corrected arrival remarks").Value;
        var editedDeparture = RtrDepartureDetails.Create(departure.DepartedAtUtc, signature, handler, null).Value;
        reloaded.UpdateReturnToRampArrival(saved.Id, editedArrival, actor, Now.AddMinutes(42)).IsSuccess.ShouldBeTrue();
        reloaded.CloseReturnToRamp(saved.Id, editedDeparture, actor, Now.AddMinutes(42)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        reloaded = await LoadAsync(db, flight.Id);
        saved = reloaded.ReturnToRamps.Single(item => item.Id == first.Id);
        saved.ArrivalDetails.ShouldBe(editedArrival);
        saved.DepartureDetails.ShouldBe(editedDeparture);
        saved.EndedByUserId.ShouldBe(closer);
        var version = reloaded.RowVersion.ToArray();
        reloaded.UpdateReturnToRampArrival(saved.Id, saved.ArrivalDetails, Guid.NewGuid(), Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        reloaded.CloseReturnToRamp(saved.Id, saved.DepartureDetails, Guid.NewGuid(), Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        (await db.SaveChangesAsync()).ShouldBe(0);
        reloaded.RowVersion.ShouldBe(version);
        saved.UpdatedAtUtc.ShouldBe(Now.AddMinutes(42));
    }

    [Fact]
    public async Task Unified_signature_migration_backfills_legacy_normal_values_and_preserves_all_existing_rtr_metadata()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync(ReturnMigration);
        var previous = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var flight = ScheduleFlight();
        var creator = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var firstSubmitter = Employee();
        var secondSubmitter = Employee();
        var handler = Employee();
        const string legacyKey = @"legacy\capture/recorded.png";
        var firstLegacySignature = CustomerSignature.Create("display-not-stored.png", legacyKey, "image/png", 512,
            Now.AddMinutes(-20)).Value;
        var firstDetails = DepartureDetails.Create(Now.AddMinutes(-12), firstLegacySignature, handler, "Original remarks").Value;
        var first = flight.SubmitDeparture(firstSubmitter, firstDetails, creator, Now.AddMinutes(-10)).Value;
        var correctedDetails = DepartureDetails.Create(firstDetails.Atd, firstLegacySignature, handler, "Corrected remarks").Value;
        flight.SubmitDeparture(firstSubmitter, correctedDetails, editor, Now.AddMinutes(-8)).IsSuccess.ShouldBeTrue();
        var secondLegacySignature = CustomerSignature.Create("second-display-not-stored.jpg", "legacy/", "image/jpeg", 2048,
            Now.AddMinutes(-19)).Value;
        var second = flight.SubmitDeparture(secondSubmitter,
            DepartureDetails.Create(Now.AddMinutes(-11), secondLegacySignature, handler, null).Value,
            creator, Now.AddMinutes(-5)).Value;
        var signed = flight.StartReturnToRamp(Arrival(), creator, Now).Value;
        var signedWork = AddWork(flight, signed.Id, creator, Now, Now);
        var storedReturnSignature = CustomerSignature.Create("preserved-customer-display.png", "returns/opaque-key.png", "image/png", 4096,
            Now.AddMinutes(29).ToOffset(TimeSpan.FromHours(3))).Value;
        flight.CloseReturnToRamp(signed.Id, ReturnDeparture(storedReturnSignature), editor, Now.AddMinutes(31)).IsSuccess.ShouldBeTrue();
        var unsigned = flight.StartReturnToRamp(Arrival(Now.AddMinutes(40)), creator, Now.AddMinutes(41)).Value;
        AddWork(flight, unsigned.Id, creator, Now.AddMinutes(42), Now.AddMinutes(40));
        flight.CloseReturnToRamp(unsigned.Id,
            RtrDepartureDetails.Create(Now.AddMinutes(70), null, handler, "No signature provided").Value,
            editor, Now.AddMinutes(71)).IsSuccess.ShouldBeTrue();
        await PreRtrMigrationSeed.InsertFlightOwnerAsync(db, flight, includesReturnToRampSequence: true);
        // The historical normal-departure schema has only key, MIME, and size. RTR already stores all five fields.
        foreach (var submission in flight.DepartureSubmissions)
            PreRtrMigrationSeed.MarkDepartureUnchanged(db, submission);
        await db.SaveChangesAsync();
        foreach (var submission in flight.DepartureSubmissions)
            await PreRtrMigrationSeed.InsertDepartureAsync(db, submission);
        var version = flight.RowVersion.ToArray();
        var rootUpdatedAt = flight.UpdatedAtUtc;
        db.ChangeTracker.Clear();

        await db.Database.MigrateAsync(SignatureMigration);

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        applied.Length.ShouldBe(previous.Length + 1);
        applied.Take(previous.Length).ShouldBe(previous);
        applied.Last().ShouldBe(SignatureMigration);
        // Verify the historical boundary before loading with the complete current model.
        await db.Database.MigrateAsync();
        var reloaded = await LoadAsync(db, flight.Id);
        reloaded.RowVersion.ShouldBe(version);
        reloaded.UpdatedAtUtc.ShouldBe(rootUpdatedAt);
        reloaded.LastReturnToRampSequence.ShouldBe(2);
        reloaded.DepartureSubmissions.Count.ShouldBe(2);
        reloaded.DepartureSubmissions.ShouldAllBe(item => !item.IsAccepted);
        var savedFirst = reloaded.DepartureSubmissions.Single(item => item.Id == first.Id);
        savedFirst.SubmittedByEmployee.ShouldBe(firstSubmitter);
        savedFirst.Details.Atd.ShouldBe(correctedDetails.Atd);
        savedFirst.Details.HandledByEmployee.ShouldBe(handler);
        savedFirst.Details.Remarks.ShouldBe("Corrected remarks");
        savedFirst.CreatedByUserId.ShouldBe(creator);
        savedFirst.CreatedAtUtc.ShouldBe(Now.AddMinutes(-10));
        savedFirst.UpdatedByUserId.ShouldBe(editor);
        savedFirst.UpdatedAtUtc.ShouldBe(Now.AddMinutes(-8));
        savedFirst.Details.CustomerSignature.ShouldBe(CustomerSignature.Create("recorded.png", legacyKey, "image/png", 512,
            savedFirst.CreatedAtUtc).Value);
        savedFirst.Details.CustomerSignature.SignedAtUtc.ShouldNotBe(firstLegacySignature.SignedAtUtc);
        savedFirst.Details.CustomerSignature.SignedAtUtc.ShouldNotBe(savedFirst.UpdatedAtUtc!.Value);
        var savedSecond = reloaded.DepartureSubmissions.Single(item => item.Id == second.Id);
        savedSecond.SubmittedByEmployee.ShouldBe(secondSubmitter);
        savedSecond.CreatedByUserId.ShouldBe(creator);
        savedSecond.CreatedAtUtc.ShouldBe(Now.AddMinutes(-5));
        savedSecond.UpdatedByUserId.ShouldBeNull();
        savedSecond.UpdatedAtUtc.ShouldBeNull();
        savedSecond.Details.CustomerSignature.ShouldBe(CustomerSignature.Create("customer-signature", "legacy/", "image/jpeg", 2048,
            savedSecond.CreatedAtUtc).Value);
        var preservedSigned = reloaded.ReturnToRamps.Single(item => item.Id == signed.Id);
        preservedSigned.ArrivalDetails.ShouldBe(signed.ArrivalDetails);
        preservedSigned.DepartureDetails.ShouldBe(signed.DepartureDetails);
        preservedSigned.DepartureDetails.ShouldNotBeNull().CustomerSignature.ShouldBe(storedReturnSignature);
        preservedSigned.CreatedByUserId.ShouldBe(creator);
        preservedSigned.CreatedAtUtc.ShouldBe(signed.CreatedAtUtc);
        preservedSigned.EndedByUserId.ShouldBe(editor);
        preservedSigned.UpdatedByUserId.ShouldBe(editor);
        preservedSigned.UpdatedAtUtc.ShouldBe(Now.AddMinutes(31));
        preservedSigned.Services.ShouldHaveSingleItem().Id.ShouldBe(signedWork.Service.Id);
        preservedSigned.Tasks.ShouldHaveSingleItem().Id.ShouldBe(signedWork.Task.Id);
        var preservedUnsigned = reloaded.ReturnToRamps.Single(item => item.Id == unsigned.Id);
        preservedUnsigned.ArrivalDetails.ShouldBe(unsigned.ArrivalDetails);
        preservedUnsigned.DepartureDetails.ShouldBe(unsigned.DepartureDetails);
        preservedUnsigned.DepartureDetails.ShouldNotBeNull().CustomerSignature.ShouldBeNull();
        preservedUnsigned.CreatedByUserId.ShouldBe(creator);
        preservedUnsigned.EndedByUserId.ShouldBe(editor);
        preservedUnsigned.UpdatedAtUtc.ShouldBe(Now.AddMinutes(71));
    }

    [Theory]
    [InlineData("oversized-key")]
    [InlineData("oversized-mime")]
    [InlineData("default-created-at")]
    public async Task Signature_migration_preflight_preserves_legacy_data_when_metadata_needs_resolution(string invalidData)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync(ReturnMigration);
        var previous = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var actor = Guid.NewGuid();
        var submitter = Employee();
        var flight = ScheduleFlight();
        var details = Departure();
        var submission = flight.SubmitDeparture(submitter, details, actor, Now).Value;
        await PreRtrMigrationSeed.InsertFlightOwnerAsync(db, flight, includesReturnToRampSequence: true);
        PreRtrMigrationSeed.MarkDepartureUnchanged(db, submission);
        await db.SaveChangesAsync();
        await PreRtrMigrationSeed.InsertDepartureAsync(db, submission);
        var version = flight.RowVersion.ToArray();
        var legacyKey = invalidData == "oversized-key" ? new string('K', 501) : details.CustomerSignature.StorageKey;
        var legacyMime = invalidData == "oversized-mime" ? new string('M', 101) : details.CustomerSignature.ContentType;
        var legacyCreatedAt = invalidData == "default-created-at" ? default : Now;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [operations].[flight_departure_submissions]
            SET [CustomerSignatureStorageKey] = {legacyKey}, [CustomerSignatureContentType] = {legacyMime}, [CreatedAtUtc] = {legacyCreatedAt}
            WHERE [Id] = {submission.Id}
            """);
        db.ChangeTracker.Clear();

        var rejected = await Should.ThrowAsync<SqlException>(() => db.Database.MigrateAsync());

        rejected.Number.ShouldBe(51000);
        rejected.Message.ShouldContain("CustomerSignature unification:");
        (await db.Database.GetAppliedMigrationsAsync()).ShouldBe(previous);
        var persisted = await db.Set<FlightDepartureSubmission>().Where(item => item.Id == submission.Id)
            .Select(item => new
            {
                item.FlightId,
                StaffMemberId = item.SubmittedByEmployee.StaffMemberId,
                item.IsAccepted,
                item.CreatedByUserId,
                item.CreatedAtUtc,
                item.UpdatedByUserId,
                item.UpdatedAtUtc,
                item.Details.Atd,
                item.Details.CustomerSignature.StorageKey,
                item.Details.CustomerSignature.ContentType,
                item.Details.CustomerSignature.SizeBytes
            }).SingleAsync();
        persisted.FlightId.ShouldBe(flight.Id);
        persisted.StaffMemberId.ShouldBe(submitter.StaffMemberId);
        persisted.IsAccepted.ShouldBeTrue();
        persisted.CreatedByUserId.ShouldBe(actor);
        persisted.CreatedAtUtc.ShouldBe(legacyCreatedAt);
        persisted.UpdatedByUserId.ShouldBeNull();
        persisted.UpdatedAtUtc.ShouldBeNull();
        persisted.Atd.ShouldBe(details.Atd);
        persisted.StorageKey.ShouldBe(legacyKey);
        persisted.ContentType.ShouldBe(legacyMime);
        persisted.SizeBytes.ShouldBe(details.CustomerSignature.SizeBytes);
        (await db.Flights.Where(item => item.Id == flight.Id).Select(item => item.RowVersion).SingleAsync()).ShouldBe(version);
        await db.Database.OpenConnectionAsync();
        await using var schemaCheck = db.Database.GetDbConnection().CreateCommand();
        schemaCheck.CommandText = """
            SELECT COUNT(*) FROM sys.columns
            WHERE [object_id] = OBJECT_ID(N'[operations].[flight_departure_submissions]')
                AND [name] IN (N'CustomerSignatureFileName', N'CustomerSignedAtUtc')
            """;
        Convert.ToInt32(await schemaCheck.ExecuteScalarAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Shared_signature_values_are_independent_for_normal_and_rtr_departures_and_track_all_metadata_changes()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var actor = Guid.NewGuid();
        var editor = Guid.NewGuid();
        var flight = ScheduleFlight();
        var submitter = Employee();
        var handler = Employee();
        var shared = Signature();
        var normalDetails = DepartureDetails.Create(Now.AddMinutes(-10), shared, handler, "Normal departure").Value;
        var normal = flight.SubmitDeparture(submitter, normalDetails, actor, Now).Value;
        var occurrence = flight.StartReturnToRamp(Arrival(), actor, Now).Value;
        AddWork(flight, occurrence.Id, actor, Now, Now);
        var returnDetails = RtrDepartureDetails.Create(Now.AddMinutes(30), shared, handler, "RTR departure").Value;
        flight.CloseReturnToRamp(occurrence.Id, returnDetails, actor, Now.AddMinutes(31)).IsSuccess.ShouldBeTrue();
        normal.Details.CustomerSignature.ShouldNotBeSameAs(shared);
        occurrence.DepartureDetails.ShouldNotBeNull().CustomerSignature.ShouldNotBeSameAs(shared);
        normal.Details.CustomerSignature.ShouldNotBeSameAs(occurrence.DepartureDetails.CustomerSignature);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadAsync(db, flight.Id);
        var savedNormal = reloaded.DepartureSubmissions.ShouldHaveSingleItem();
        var savedReturn = reloaded.ReturnToRamps.ShouldHaveSingleItem();
        savedNormal.Details.CustomerSignature.ShouldBe(shared);
        savedReturn.DepartureDetails.ShouldNotBeNull().CustomerSignature.ShouldBe(shared);
        savedNormal.Details.CustomerSignature.ShouldNotBeSameAs(savedReturn.DepartureDetails.CustomerSignature);
        var originalVersion = reloaded.RowVersion.ToArray();
        var changedSigningTime = CustomerSignature.Create(shared.FileName, shared.StorageKey, shared.ContentType,
            shared.SizeBytes, shared.SignedAtUtc.AddSeconds(1)).Value;
        reloaded.SubmitDeparture(submitter, DepartureDetails.Create(savedNormal.Details.Atd, changedSigningTime,
            savedNormal.Details.HandledByEmployee, savedNormal.Details.Remarks).Value, editor, Now.AddMinutes(32)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadAsync(db, flight.Id);
        savedNormal = reloaded.DepartureSubmissions.ShouldHaveSingleItem();
        savedReturn = reloaded.ReturnToRamps.ShouldHaveSingleItem();
        savedNormal.Details.CustomerSignature.ShouldBe(changedSigningTime);
        savedNormal.UpdatedByUserId.ShouldBe(editor);
        savedNormal.UpdatedAtUtc.ShouldBe(Now.AddMinutes(32));
        savedReturn.DepartureDetails.ShouldNotBeNull().CustomerSignature.ShouldBe(shared);
        savedReturn.UpdatedAtUtc.ShouldBe(Now.AddMinutes(31));
        reloaded.RowVersion.SequenceEqual(originalVersion).ShouldBeFalse();
        var changedFileName = CustomerSignature.Create("corrected-name.png", shared.StorageKey, shared.ContentType,
            shared.SizeBytes, shared.SignedAtUtc).Value;
        var previousVersion = reloaded.RowVersion.ToArray();
        reloaded.CloseReturnToRamp(savedReturn.Id,
            RtrDepartureDetails.Create(savedReturn.DepartureDetails.DepartedAtUtc, changedFileName,
                savedReturn.DepartureDetails.HandledByEmployee, savedReturn.DepartureDetails.Remarks).Value,
            editor, Now.AddMinutes(33)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadAsync(db, flight.Id);
        savedNormal = reloaded.DepartureSubmissions.ShouldHaveSingleItem();
        savedReturn = reloaded.ReturnToRamps.ShouldHaveSingleItem();
        savedNormal.Details.CustomerSignature.ShouldBe(changedSigningTime);
        savedNormal.UpdatedAtUtc.ShouldBe(Now.AddMinutes(32));
        savedReturn.DepartureDetails.ShouldNotBeNull().CustomerSignature.ShouldBe(changedFileName);
        savedReturn.UpdatedAtUtc.ShouldBe(Now.AddMinutes(33));
        savedReturn.EndedByUserId.ShouldBe(actor);
        reloaded.RowVersion.SequenceEqual(previousVersion).ShouldBeFalse();
        var versionBeforeNoOp = reloaded.RowVersion.ToArray();
        var equalNormal = CustomerSignature.Create(changedSigningTime.FileName, changedSigningTime.StorageKey,
            changedSigningTime.ContentType, changedSigningTime.SizeBytes,
            changedSigningTime.SignedAtUtc.ToOffset(TimeSpan.FromHours(3))).Value;
        var equalReturn = CustomerSignature.Create(changedFileName.FileName, changedFileName.StorageKey,
            changedFileName.ContentType, changedFileName.SizeBytes, changedFileName.SignedAtUtc).Value;
        reloaded.SubmitDeparture(submitter, DepartureDetails.Create(savedNormal.Details.Atd, equalNormal,
            savedNormal.Details.HandledByEmployee, savedNormal.Details.Remarks).Value,
            Guid.NewGuid(), Now.AddMinutes(34)).IsSuccess.ShouldBeTrue();
        reloaded.CloseReturnToRamp(savedReturn.Id,
            RtrDepartureDetails.Create(savedReturn.DepartureDetails.DepartedAtUtc, equalReturn,
                savedReturn.DepartureDetails.HandledByEmployee, savedReturn.DepartureDetails.Remarks).Value,
            Guid.NewGuid(), Now.AddMinutes(34)).IsSuccess.ShouldBeTrue();

        (await db.SaveChangesAsync()).ShouldBe(0);
        reloaded.RowVersion.ShouldBe(versionBeforeNoOp);
        savedNormal.UpdatedAtUtc.ShouldBe(Now.AddMinutes(32));
        savedReturn.UpdatedAtUtc.ShouldBe(Now.AddMinutes(33));
    }

    [Fact]
    public async Task Scoped_mutations_and_composite_foreign_keys_keep_normal_and_foreign_work_separate()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var actor = Guid.NewGuid();
        var flight = DepartedFlight();
        var normal = AddWork(flight, null, actor, Now, Now.AddMinutes(-30));
        var first = flight.StartReturnToRamp(Arrival(), actor, Now).Value;
        var work = AddWork(flight, first.Id, actor, Now, Now);
        flight.CloseReturnToRamp(first.Id, ReturnDeparture(), actor, Now).IsSuccess.ShouldBeTrue();
        var second = flight.StartReturnToRamp(Arrival(Now.AddMinutes(40)), actor, Now).Value;
        var otherFlight = DepartedFlight();
        var foreign = otherFlight.StartReturnToRamp(Arrival(), actor, Now).Value;
        var foreignWork = AddWork(otherFlight, foreign.Id, actor, Now, Now);
        db.Flights.AddRange(flight, otherFlight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadAsync(db, flight.Id);
        var version = reloaded.RowVersion.ToArray();
        reloaded.UpdateService(work.Service.Id, work.Service.Service, null, work.Service.Window, "Outside scope", actor, Now)
            .IsFailure.ShouldBeTrue();
        reloaded.UpdateService(normal.Service.Id, normal.Service.Service, null, normal.Service.Window, "Outside scope", actor, Now,
            returnToRampId: first.Id).IsFailure.ShouldBeTrue();
        reloaded.UpdateTask(work.Task.Id, work.Task.TaskType, work.Task.AtaChapter, null, work.Task.Window, "Outside scope", actor, Now)
            .IsFailure.ShouldBeTrue();
        reloaded.UpdateTask(work.Task.Id, work.Task.TaskType, work.Task.AtaChapter, null, work.Task.Window, "Outside scope", actor, Now,
            returnToRampId: foreign.Id).IsFailure.ShouldBeTrue();
        reloaded.RemoveServiceAttachment(work.Service.Id, foreignWork.Service.Attachments.Single().Id, actor, Now,
            returnToRampId: first.Id).IsFailure.ShouldBeTrue();
        reloaded.RemoveTaskTool(work.Task.Id, foreignWork.Task.Tools.Single().Id, actor, Now,
            returnToRampId: first.Id).IsFailure.ShouldBeTrue();
        reloaded.RemoveReturnToRamp(foreign.Id, actor, Now).IsFailure.ShouldBeTrue();
        (await db.SaveChangesAsync()).ShouldBe(0);
        reloaded.RowVersion.ShouldBe(version);

        var invalidServiceOwner = await Should.ThrowAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [operations].[flight_services] SET [ReturnToRampId] = {foreign.Id} WHERE [Id] = {work.Service.Id}
            """));
        invalidServiceOwner.Number.ShouldBe(547);
        var invalidTaskOwner = await Should.ThrowAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [operations].[flight_tasks] SET [ReturnToRampId] = {foreign.Id} WHERE [Id] = {work.Task.Id}
            """));
        invalidTaskOwner.Number.ShouldBe(547);
        var duplicateSequence = await Should.ThrowAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [operations].[flight_return_to_ramps] SET [Sequence] = {second.Sequence} WHERE [Id] = {first.Id}
            """));
        new[] { 2601, 2627 }.ShouldContain(duplicateSequence.Number);
        var secondOpen = await Should.ThrowAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [operations].[flight_return_to_ramps] SET [RtrDepartureUtc] = NULL WHERE [Id] = {first.Id}
            """));
        new[] { 2601, 2627 }.ShouldContain(secondOpen.Number);
        db.ChangeTracker.Clear();

        reloaded = await LoadAsync(db, flight.Id);
        reloaded.RowVersion.ShouldBe(version);
        reloaded.Services.ShouldHaveSingleItem().Id.ShouldBe(normal.Service.Id);
        reloaded.Tasks.ShouldHaveSingleItem().Id.ShouldBe(normal.Task.Id);
        reloaded.ReturnToRamps.Single(item => item.Id == first.Id).Services.ShouldHaveSingleItem().Id.ShouldBe(work.Service.Id);
        reloaded.ReturnToRamps.Single(item => item.Id == first.Id).Tasks.ShouldHaveSingleItem().Id.ShouldBe(work.Task.Id);
        reloaded.ReturnToRamps.Single(item => item.Id == first.Id).IsOpen.ShouldBeFalse();
        reloaded.ReturnToRamps.Single(item => item.Id == second.Id).IsOpen.ShouldBeTrue();
        var preservedForeign = await LoadAsync(db, otherFlight.Id);
        preservedForeign.ReturnToRamps.ShouldHaveSingleItem().Services.ShouldHaveSingleItem().Id.ShouldBe(foreignWork.Service.Id);
        preservedForeign.ReturnToRamps.Single().Tasks.ShouldHaveSingleItem().Id.ShouldBe(foreignWork.Task.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_starts_keep_one_open_occurrence_and_one_sequence_increment(bool synchronous)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = DepartedFlight();
        db.Flights.Add(flight);
        await SaveAsync(db, synchronous);
        db.ChangeTracker.Clear();
        await using var competing = fixture.CreateContext(db.Database.GetDbConnection().Database);
        var winner = await LoadAsync(db, flight.Id);
        var stale = await LoadAsync(competing, flight.Id);
        var actor = Guid.NewGuid();
        var accepted = winner.StartReturnToRamp(Arrival(), actor, Now).Value;
        stale.StartReturnToRamp(Arrival(), actor, Now).IsSuccess.ShouldBeTrue();
        await SaveAsync(db, synchronous);

        var rejected = await Should.ThrowAsync<DbUpdateException>(() => SaveAsync(competing, synchronous));

        if (rejected is not DbUpdateConcurrencyException)
        {
            var uniqueViolation = rejected.InnerException.ShouldBeOfType<SqlException>();
            new[] { 2601, 2627 }.ShouldContain(uniqueViolation.Number);
        }
        db.ChangeTracker.Clear();
        var reloaded = await LoadAsync(db, flight.Id);
        reloaded.ReturnToRamps.ShouldHaveSingleItem().Id.ShouldBe(accepted.Id);
        reloaded.ReturnToRamps.Single().IsOpen.ShouldBeTrue();
        reloaded.ReturnToRamps.Single().Sequence.ShouldBe(1);
        reloaded.LastReturnToRampSequence.ShouldBe(1);
        reloaded.StartReturnToRamp(Arrival(), actor, Now).IsFailure.ShouldBeTrue();
        (await SaveAsync(db, synchronous)).ShouldBe(0);
    }

    [Fact]
    public async Task Departure_must_be_recorded_before_start_and_stale_eligibility_needs_a_reload()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var flight = ScheduleFlight();
        flight.UpdatePlanning(flight.Schedule, flight.AircraftType, flight.PlannedServices.Select(item => item.Service).ToArray(),
            null, Now).IsSuccess.ShouldBeTrue();
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await using var competing = fixture.CreateContext(db.Database.GetDbConnection().Database);
        var recordingDeparture = await LoadAsync(db, flight.Id);
        var stale = await LoadAsync(competing, flight.Id);
        var version = stale.RowVersion.ToArray();
        stale.StartReturnToRamp(Arrival(), Guid.NewGuid(), Now).IsFailure.ShouldBeTrue();
        recordingDeparture.SubmitDeparture(Employee(), Departure(), Guid.NewGuid(), Now).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        stale.StartReturnToRamp(Arrival(), Guid.NewGuid(), Now).IsFailure.ShouldBeTrue();
        (await competing.SaveChangesAsync()).ShouldBe(0);
        stale.RowVersion.ShouldBe(version);
        stale.ReturnToRamps.ShouldBeEmpty();
        competing.ChangeTracker.Clear();
        var current = await LoadAsync(competing, flight.Id);
        current.RowVersion.SequenceEqual(version).ShouldBeFalse();
        current.DepartureSubmissions.ShouldHaveSingleItem();
        current.StartReturnToRamp(Arrival(), Guid.NewGuid(), Now).IsSuccess.ShouldBeTrue();
        await competing.SaveChangesAsync();
        db.ChangeTracker.Clear();
        (await LoadAsync(db, flight.Id)).ReturnToRamps.ShouldHaveSingleItem().Sequence.ShouldBe(1);
    }

    [Fact]
    public async Task Closing_current_and_starting_next_occurrence_in_one_save_respects_the_open_unique_index()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var actor = Guid.NewGuid();
        var flight = DepartedFlight();
        var first = flight.StartReturnToRamp(Arrival(), actor, Now).Value;
        AddWork(flight, first.Id, actor, Now, Now);
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadAsync(db, flight.Id);
        reloaded.CloseReturnToRamp(first.Id, ReturnDeparture(), actor, Now).IsSuccess.ShouldBeTrue();
        var second = reloaded.StartReturnToRamp(Arrival(Now.AddMinutes(40)), actor, Now).Value;

        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        reloaded = await LoadAsync(db, flight.Id);
        reloaded.LastReturnToRampSequence.ShouldBe(2);
        reloaded.ReturnToRamps.Single(item => item.Id == first.Id).IsOpen.ShouldBeFalse();
        reloaded.ReturnToRamps.Single(item => item.Id == second.Id).IsOpen.ShouldBeTrue();
        reloaded.ReturnToRamps.Count(item => item.IsOpen).ShouldBe(1);
        reloaded.ReturnToRamps.Single(item => item.Id == first.Id).Services.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("arrival-update")]
    [InlineData("signature-remove")]
    [InlineData("service-performer-update")]
    [InlineData("service-attachment-remove")]
    [InlineData("task-material-update")]
    [InlineData("task-tool-remove")]
    [InlineData("task-ata-remove")]
    [InlineData("empty-rtr-remove")]
    public async Task Nested_occurrence_changes_force_flight_version_checks_when_actor_and_clock_match(string mutation)
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var actor = Guid.NewGuid();
        var matchingClock = Now.AddMinutes(50);
        var flight = DepartedFlight();
        var occurrence = flight.StartReturnToRamp(Arrival(), actor, matchingClock).Value;
        if (mutation != "empty-rtr-remove")
        {
            AddWork(flight, occurrence.Id, actor, matchingClock, Now);
            flight.CloseReturnToRamp(occurrence.Id, ReturnDeparture(Signature()), actor, matchingClock).IsSuccess.ShouldBeTrue();
        }
        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await using var competing = fixture.CreateContext(db.Database.GetDbConnection().Database);
        var winner = await LoadAsync(db, flight.Id);
        var stale = await LoadAsync(competing, flight.Id);
        var version = winner.RowVersion.ToArray();
        var current = winner.ReturnToRamps.ShouldHaveSingleItem();
        winner.UpdatedAtUtc.ShouldBe(matchingClock);
        var service = current.Services.SingleOrDefault();
        var task = current.Tasks.SingleOrDefault();
        switch (mutation)
        {
            case "arrival-update":
                winner.UpdateReturnToRampArrival(current.Id,
                    RtrArrivalDetails.Create(current.ArrivalDetails.ArrivedAtUtc, current.ArrivalDetails.ReceivedByEmployee, "Corrected arrival").Value,
                    actor, matchingClock).IsSuccess.ShouldBeTrue();
                break;
            case "signature-remove":
                var departure = current.DepartureDetails!;
                winner.CloseReturnToRamp(current.Id, RtrDepartureDetails.Create(departure.DepartedAtUtc, null,
                    departure.HandledByEmployee, departure.Remarks).Value, actor, matchingClock).IsSuccess.ShouldBeTrue();
                break;
            case "service-performer-update":
                var performer = service!.Performers.Single();
                winner.UpdateServicePerformer(service.Id, performer.Id, performer.Employee,
                    TimeWindow.Create(performer.Window.FromUtc, Now.AddMinutes(8)).Value, actor, matchingClock,
                    returnToRampId: current.Id).IsSuccess.ShouldBeTrue();
                break;
            case "service-attachment-remove":
                winner.RemoveServiceAttachment(service!.Id, service.Attachments.Single().Id, actor, matchingClock,
                    returnToRampId: current.Id).IsSuccess.ShouldBeTrue();
                break;
            case "task-material-update":
                var material = task!.Materials.Single();
                winner.UpdateTaskMaterial(task.Id, material.Id, material.Material, Quantity(13.50m), material.Notes,
                    actor, matchingClock, returnToRampId: current.Id).IsSuccess.ShouldBeTrue();
                break;
            case "task-tool-remove":
                winner.RemoveTaskTool(task!.Id, task.Tools.Single().Id, actor, matchingClock,
                    returnToRampId: current.Id).IsSuccess.ShouldBeTrue();
                break;
            case "task-ata-remove":
                winner.UpdateTask(task!.Id, task.TaskType, null, task.CustomerReferenceNumber, task.Window, task.Notes,
                    actor, matchingClock, returnToRampId: current.Id).IsSuccess.ShouldBeTrue();
                break;
            default:
                winner.RemoveReturnToRamp(current.Id, actor, matchingClock).IsSuccess.ShouldBeTrue();
                break;
        }
        stale.UpdatePlanning(stale.Schedule, new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A321"),
            stale.PlannedServices.Select(item => new ServiceSnapshot(item.Service.ServiceId, item.Service.Name)).ToArray(),
            null, matchingClock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        var staleWriteError = await Record.ExceptionAsync(() => competing.SaveChangesAsync());
        Assert.True(staleWriteError is DbUpdateConcurrencyException, staleWriteError?.ToString() ?? "Expected a stale write to fail.");

        db.ChangeTracker.Clear();
        var reloaded = await LoadAsync(db, flight.Id);
        reloaded.RowVersion.SequenceEqual(version).ShouldBeFalse();
        reloaded.UpdatedAtUtc.ShouldBe(matchingClock);
        reloaded.AircraftType.ShouldBe(flight.AircraftType);
        reloaded.LastReturnToRampSequence.ShouldBe(1);
        if (mutation == "empty-rtr-remove")
        {
            reloaded.ReturnToRamps.ShouldBeEmpty();
            return;
        }
        var retained = reloaded.ReturnToRamps.ShouldHaveSingleItem();
        retained.UpdatedByUserId.ShouldBe(actor);
        retained.UpdatedAtUtc.ShouldBe(matchingClock);
        if (mutation == "arrival-update")
            retained.ArrivalDetails.Remarks.ShouldBe("Corrected arrival");
        if (mutation == "signature-remove")
            retained.DepartureDetails.ShouldNotBeNull().CustomerSignature.ShouldBeNull();
        if (mutation == "service-performer-update")
            retained.Services.ShouldHaveSingleItem().Performers.ShouldHaveSingleItem().Window.ToUtc.ShouldBe(Now.AddMinutes(8));
        if (mutation == "service-attachment-remove")
            retained.Services.ShouldHaveSingleItem().Attachments.ShouldBeEmpty();
        if (mutation == "task-material-update")
            retained.Tasks.ShouldHaveSingleItem().Materials.ShouldHaveSingleItem().Usage.ShouldBe(Quantity(13.50m));
        if (mutation == "task-tool-remove")
            retained.Tasks.ShouldHaveSingleItem().Tools.ShouldBeEmpty();
        if (mutation == "task-ata-remove")
            retained.Tasks.ShouldHaveSingleItem().AtaChapter.ShouldBeNull();
    }

    [Fact]
    public async Task Removing_one_occurrence_deletes_only_its_work_and_does_not_reuse_its_sequence()
    {
        await using var db = fixture.CreateContext();
        await db.Database.MigrateAsync();
        var actor = Guid.NewGuid();
        var flight = DepartedFlight();
        var normal = AddWork(flight, null, actor, Now, Now.AddMinutes(-30));
        var first = flight.StartReturnToRamp(Arrival(), actor, Now).Value;
        var removedWork = AddWork(flight, first.Id, actor, Now, Now);
        flight.CloseReturnToRamp(first.Id, ReturnDeparture(Signature()), actor, Now).IsSuccess.ShouldBeTrue();
        var second = flight.StartReturnToRamp(Arrival(Now.AddMinutes(40)), actor, Now).Value;
        var retainedWork = AddWork(flight, second.Id, actor, Now, Now.AddMinutes(40));
        var otherFlight = DepartedFlight();
        var foreign = otherFlight.StartReturnToRamp(Arrival(), actor, Now).Value;
        var foreignWork = AddWork(otherFlight, foreign.Id, actor, Now, Now);
        db.Flights.AddRange(flight, otherFlight);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await LoadAsync(db, flight.Id);
        var removed = reloaded.RemoveReturnToRamp(first.Id, actor, Now).Value;
        removed.DepartureDetails.ShouldNotBeNull().CustomerSignature.ShouldNotBeNull().StorageKey.ShouldBe("signatures/return.png");
        reloaded.LastReturnToRampSequence.ShouldBe(2);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadAsync(db, flight.Id);
        reloaded.ReturnToRamps.ShouldHaveSingleItem().Id.ShouldBe(second.Id);
        reloaded.ReturnToRamps.Single().Services.ShouldHaveSingleItem().Id.ShouldBe(retainedWork.Service.Id);
        reloaded.ReturnToRamps.Single().Tasks.ShouldHaveSingleItem().Id.ShouldBe(retainedWork.Task.Id);
        reloaded.Services.ShouldHaveSingleItem().Id.ShouldBe(normal.Service.Id);
        reloaded.Tasks.ShouldHaveSingleItem().Id.ShouldBe(normal.Task.Id);
        reloaded.LastReturnToRampSequence.ShouldBe(2);
        (await db.Set<FlightService>().AnyAsync(item => item.Id == removedWork.Service.Id)).ShouldBeFalse();
        (await db.Set<FlightServiceAttachment>().AnyAsync(item => item.FlightServiceId == removedWork.Service.Id)).ShouldBeFalse();
        (await db.Set<FlightServicePerformer>().AnyAsync(item => item.FlightServiceId == removedWork.Service.Id)).ShouldBeFalse();
        (await db.Set<FlightTask>().AnyAsync(item => item.Id == removedWork.Task.Id)).ShouldBeFalse();
        (await db.Set<FlightTaskTool>().AnyAsync(item => item.FlightTaskId == removedWork.Task.Id)).ShouldBeFalse();
        (await db.Set<FlightTaskMaterial>().AnyAsync(item => item.FlightTaskId == removedWork.Task.Id)).ShouldBeFalse();
        (await db.Set<FlightTaskGeneralSupport>().AnyAsync(item => item.FlightTaskId == removedWork.Task.Id)).ShouldBeFalse();
        (await db.Set<FlightTaskPerformer>().AnyAsync(item => item.FlightTaskId == removedWork.Task.Id)).ShouldBeFalse();
        (await db.Set<FlightTaskAttachment>().AnyAsync(item => item.FlightTaskId == removedWork.Task.Id)).ShouldBeFalse();
        reloaded.CloseReturnToRamp(second.Id,
            RtrDepartureDetails.Create(Now.AddMinutes(70), null, Employee(), null).Value, actor, Now).IsSuccess.ShouldBeTrue();
        var third = reloaded.StartReturnToRamp(Arrival(Now.AddMinutes(80)), actor, Now).Value;
        third.Sequence.ShouldBe(3);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        reloaded = await LoadAsync(db, flight.Id);
        reloaded.LastReturnToRampSequence.ShouldBe(3);
        reloaded.ReturnToRamps.Select(item => item.Sequence).OrderBy(sequence => sequence).ShouldBe([2, 3]);
        reloaded.Services.ShouldHaveSingleItem().Attachments.ShouldHaveSingleItem().Id.ShouldBe(normal.Service.Attachments.Single().Id);
        var other = await LoadAsync(db, otherFlight.Id);
        other.ReturnToRamps.ShouldHaveSingleItem().Services.ShouldHaveSingleItem().Id.ShouldBe(foreignWork.Service.Id);
        other.ReturnToRamps.Single().Tasks.ShouldHaveSingleItem().Id.ShouldBe(foreignWork.Task.Id);
        db.Flights.Remove(reloaded);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        (await db.Set<FlightReturnToRamp>().AnyAsync(item => item.FlightId == flight.Id)).ShouldBeFalse();
        (await db.Set<FlightService>().AnyAsync(item => item.FlightId == flight.Id)).ShouldBeFalse();
        (await db.Set<FlightTask>().AnyAsync(item => item.FlightId == flight.Id)).ShouldBeFalse();
        other = await LoadAsync(db, otherFlight.Id);
        other.ReturnToRamps.ShouldHaveSingleItem().Services.ShouldHaveSingleItem().Id.ShouldBe(foreignWork.Service.Id);
        other.ReturnToRamps.Single().Tasks.ShouldHaveSingleItem().Id.ShouldBe(foreignWork.Task.Id);
    }

    private static Task<Flight> LoadAsync(OperationsDbContext db, Guid id) =>
        db.Flights.Include(flight => flight.PlannedServices).Include(flight => flight.AssignedEmployees)
            .Include(flight => flight.ArrivalSubmissions).Include(flight => flight.DepartureSubmissions)
            .Include(flight => flight.CancellationSubmissions).Include(flight => flight.ReturnToRamps)
            .Include(flight => flight.ServiceRecords).ThenInclude(service => service.Performers)
            .Include(flight => flight.ServiceRecords).ThenInclude(service => service.Attachments)
            .Include(flight => flight.TaskRecords).ThenInclude(task => task.Tools)
            .Include(flight => flight.TaskRecords).ThenInclude(task => task.Materials)
            .Include(flight => flight.TaskRecords).ThenInclude(task => task.GeneralSupports)
            .Include(flight => flight.TaskRecords).ThenInclude(task => task.Performers)
            .Include(flight => flight.TaskRecords).ThenInclude(task => task.Attachments)
            .AsSplitQuery().SingleAsync(flight => flight.Id == id);

    private static (FlightService Service, FlightTask Task) AddWork(Flight flight, Guid? returnToRampId,
        Guid actor, DateTimeOffset auditTime, DateTimeOffset baseTime)
    {
        var start = baseTime.AddMinutes(2);
        var end = baseTime.AddMinutes(10);
        var open = TimeWindow.Create(start, null).Value;
        var closed = TimeWindow.Create(start, end).Value;
        var duration = ResourceUsage.Create(ResourceCalculationType.Duration, null, start, end).Value;
        var employee = Employee();
        var service = flight.AddService(new ServiceSnapshot(Guid.NewGuid(), "Inspection support"), "SERVICE-REF", open,
            "Service notes", actor, auditTime, returnToRampId: returnToRampId).Value;
        flight.AddServicePerformer(service.Id, employee, closed, actor, auditTime, returnToRampId: returnToRampId).IsSuccess.ShouldBeTrue();
        flight.AddServiceAttachment(service.Id, Attachment("services/report.jpg"), actor, auditTime,
            returnToRampId: returnToRampId).IsSuccess.ShouldBeTrue();
        flight.UpdateService(service.Id, service.Service, service.CustomerReferenceNumber, closed, service.Notes, actor,
            auditTime, returnToRampId: returnToRampId).IsSuccess.ShouldBeTrue();
        var task = flight.AddTask(FlightTaskType.Major, new AtaChapterSnapshot(Guid.NewGuid(), "32", "Landing gear"),
            "TASK-REF", open, "Task notes", actor, auditTime, returnToRampId: returnToRampId).Value;
        flight.AddTaskTool(task.Id, new ToolSnapshot(Guid.NewGuid(), "Torque wrench"), duration, null, actor, auditTime,
            returnToRampId: returnToRampId).IsSuccess.ShouldBeTrue();
        flight.AddTaskMaterial(task.Id, new MaterialSnapshot(Guid.NewGuid(), "Lubricant"), Quantity(12.25m), null,
            actor, auditTime, returnToRampId: returnToRampId).IsSuccess.ShouldBeTrue();
        flight.AddTaskGeneralSupport(task.Id, new GeneralSupportSnapshot(Guid.NewGuid(), "Ground support", ResourceCalculationType.Duration),
            duration, null, actor, auditTime, returnToRampId: returnToRampId).IsSuccess.ShouldBeTrue();
        flight.AddTaskPerformer(task.Id, employee, closed, "Performed inspection", actor, auditTime,
            returnToRampId: returnToRampId).IsSuccess.ShouldBeTrue();
        flight.AddTaskAttachment(task.Id, Attachment("tasks/report.jpg"), actor, auditTime,
            returnToRampId: returnToRampId).IsSuccess.ShouldBeTrue();
        flight.UpdateTask(task.Id, task.TaskType, task.AtaChapter, task.CustomerReferenceNumber, closed, task.Notes,
            actor, auditTime, returnToRampId: returnToRampId).IsSuccess.ShouldBeTrue();
        return (service, task);
    }

    private static Flight DepartedFlight()
    {
        var flight = ScheduleFlight();
        flight.SubmitDeparture(Employee(), Departure(), Guid.NewGuid(), Now).IsSuccess.ShouldBeTrue();
        return flight;
    }

    private static DepartureDetails Departure() => DepartureDetails.Create(Now.AddMinutes(-10),
        CustomerSignature.Create("flight.png", "signatures/flight.png", "image/png", 1024, Now).Value, Employee(), null).Value;

    private static Flight ScheduleFlight() => Flight.ScheduleNew(
        new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"), new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh"),
        new OperationTypeSnapshot(Guid.NewGuid(), "Transit"), FlightNumber.Create("SV1020", "SV").Value,
        ScheduledTime.Create(Now.AddHours(-1), Now).Value, new AircraftTypeSnapshot(Guid.NewGuid(), "Airbus", "A320"),
        [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")], [Employee()], Guid.NewGuid(), Now.AddHours(-2)).Value;

    private static StaffMemberSnapshot Employee() => new(Guid.NewGuid(), "Ground engineer", "E100");
    private static RtrArrivalDetails Arrival(DateTimeOffset? at = null) => RtrArrivalDetails.Create(at ?? Now, Employee(), null).Value;
    private static RtrDepartureDetails ReturnDeparture(CustomerSignature? signature = null) =>
        RtrDepartureDetails.Create(Now.AddMinutes(30), signature, Employee(), null).Value;
    private static CustomerSignature Signature() => CustomerSignature.Create("customer/signature.png",
        "signatures/return.png", "image/png", 1024, Now.AddMinutes(30).ToOffset(TimeSpan.FromHours(3))).Value;
    private static ResourceUsage Quantity(decimal value) => ResourceUsage.Create(ResourceCalculationType.Quantity, value, null, null).Value;
    private static AttachmentDetails Attachment(string key) => AttachmentDetails.Create("report.jpg", key, "image/jpeg", 4096, AttachmentKind.Image).Value;
    private static async Task<int> SaveAsync(OperationsDbContext db, bool synchronous) => synchronous ? db.SaveChanges() : await db.SaveChangesAsync();

    public sealed class DatabaseFixture : IAsyncLifetime
    {
        private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

        public OperationsDbContext CreateContext(string? databaseName = null)
        {
            var connection = new SqlConnectionStringBuilder(_sql.GetConnectionString())
            {
                InitialCatalog = databaseName ?? $"FlightReturn_{Guid.NewGuid():N}"
            };
            return new OperationsDbContext(new DbContextOptionsBuilder<OperationsDbContext>()
                .UseSqlServer(connection.ConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", OperationsDbContext.Schema)).Options);
        }

        public async Task InitializeAsync() => await _sql.StartAsync();
        public async Task DisposeAsync() => await _sql.DisposeAsync();
    }
}
