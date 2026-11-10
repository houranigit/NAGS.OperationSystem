using System.Net;
using BuildingBlocks.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;
using MasterData.Contracts.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Operations.Application.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

/// <summary>
/// Flight visibility tiers: station dispatchers (station staff holding
/// <c>operations.flights.view-station</c>) see every flight at their own station without being on
/// the assigned-employee roster while staying blind to other stations. Regular station staff keep
/// the default Per-Landing-plus-assigned visibility.
/// </summary>
public sealed class FlightVisibilityScopeTests(OperationsApiFactory factory) : IClassFixture<OperationsApiFactory>
{
    private const string Base = OperationsApiFactory.Base;
    private const string MasterDataBase = OperationsApiFactory.MasterDataBase;
    private const string IdentityBase = OperationsApiFactory.IdentityBase;

    private static int _stationCounter;

    private static readonly string[] DispatcherPermissions =
    [
        "operations.flights.view",
        "operations.flights.view-station"
    ];

    private static readonly string[] RegularStaffPermissions =
    [
        "operations.flights.view"
    ];

    [Fact]
    public async Task Station_dispatcher_sees_unassigned_flights_at_own_station_but_regular_staff_do_not()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var dispatcher = await CreateStaffLoginAsync(admin, refs, DispatcherPermissions);
        var regular = await CreateStaffLoginAsync(admin, refs, RegularStaffPermissions);

        // A non-Per-Landing flight with nobody assigned.
        var flightId = await ScheduleFlightAsync(admin, refs.StationId, refs, "4100");

        // Dispatcher: full station-wide visibility without assignment.
        (await dispatcher.Client.GetAsync($"{Base}/flights/{flightId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var dispatcherList = await dispatcher.Client.GetFromJsonAsync<PagedList<FlightListItem>>($"{Base}/flights?page=1&pageSize=100");
        dispatcherList!.Items.ShouldContain(f => f.Id == flightId);

        // Regular staff without view-station keep assigned-only visibility.
        (await regular.Client.GetAsync($"{Base}/flights/{flightId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var regularList = await regular.Client.GetFromJsonAsync<PagedList<FlightListItem>>($"{Base}/flights?page=1&pageSize=100");
        regularList!.Items.ShouldNotContain(f => f.Id == flightId);

        // Batch notification targets mirror detail visibility and never echo nonexistent IDs.
        var targets = new { flightIds = new[] { flightId, Guid.NewGuid(), flightId, Guid.Empty } };
        using var dispatcherTargets = await dispatcher.Client.PostAsJsonAsync($"{Base}/flights/notification-targets", targets);
        dispatcherTargets.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await dispatcherTargets.Content.ReadFromJsonAsync<Guid[]>()).ShouldBe([flightId]);
        using var regularTargets = await regular.Client.PostAsJsonAsync($"{Base}/flights/notification-targets", targets);
        regularTargets.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await regularTargets.Content.ReadFromJsonAsync<Guid[]>()).ShouldBeEmpty();
        using var globalTargets = await admin.PostAsJsonAsync($"{Base}/flights/notification-targets", targets);
        (await globalTargets.Content.ReadFromJsonAsync<Guid[]>()).ShouldBe([flightId]);
    }

    [Fact]
    public async Task Station_dispatcher_cannot_see_flights_at_other_stations()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var otherStationId = await CreateStationAsync(admin, refs.CountryId);
        var dispatcher = await CreateStaffLoginAsync(admin, refs, DispatcherPermissions);

        var otherStationFlightId = await ScheduleFlightAsync(admin, otherStationId, refs, "4200");

        (await dispatcher.Client.GetAsync($"{Base}/flights/{otherStationFlightId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var list = await dispatcher.Client.GetFromJsonAsync<PagedList<FlightListItem>>($"{Base}/flights?page=1&pageSize=100");
        list!.Items.ShouldNotContain(f => f.Id == otherStationFlightId);
        using var targets = await dispatcher.Client.PostAsJsonAsync($"{Base}/flights/notification-targets",
            new { flightIds = new[] { otherStationFlightId } });
        targets.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await targets.Content.ReadFromJsonAsync<Guid[]>()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Customer_logos_require_a_visible_flight_without_granting_customer_catalog_access()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var dispatcher = await CreateStaffLoginAsync(admin, refs, DispatcherPermissions);
        var regular = await CreateStaffLoginAsync(admin, refs, RegularStaffPermissions);
        await ScheduleFlightAsync(admin, refs.StationId, refs, "5100");
        var path = $"{Base}/flights/customer-logos/{refs.CustomerId}";

        (await dispatcher.Client.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var beforeOptions = await admin.GetFromJsonAsync<List<CustomerLogoOption>>($"{MasterDataBase}/customers/options");
        beforeOptions!.Single(customer => customer.Id == refs.CustomerId).LogoVersion.ShouldBe("none");
        var logoBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jGQkAAAAASUVORK5CYII=");
        async Task UploadLogoAsync(byte[] content)
        {
            var customer = await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/customers/{refs.CustomerId}");
            using var form = new MultipartFormDataContent();
            using var file = new ByteArrayContent(content);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "logo.png");
            using var upload = new HttpRequestMessage(HttpMethod.Post, $"{MasterDataBase}/customers/{refs.CustomerId}/logo")
            {
                Content = form
            };
            upload.Headers.TryAddWithoutValidation("If-Match", customer!.RowVersion);
            (await admin.SendAsync(upload)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        await UploadLogoAsync(logoBytes);
        var options = await admin.GetFromJsonAsync<List<CustomerLogoOption>>($"{MasterDataBase}/customers/options");
        var version = options!.Single(customer => customer.Id == refs.CustomerId).LogoVersion;
        version.ShouldNotBeNull();
        version.ShouldNotBe("none");
        var flights = await dispatcher.Client.GetFromJsonAsync<PagedList<FlightListItem>>($"{Base}/flights");
        flights!.Items.Single(flight => flight.CustomerId == refs.CustomerId).CustomerLogoVersion.ShouldBe(version);

        using var logo = await dispatcher.Client.GetAsync($"{path}?expectedVersion={version}");
        logo.StatusCode.ShouldBe(HttpStatusCode.OK);
        logo.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await logo.Content.ReadAsByteArrayAsync()).ShouldBe(logoBytes);
        logo.Headers.ETag!.Tag.ShouldBe($"\"{version}\"");
        logo.Headers.ETag.IsWeak.ShouldBeFalse();
        using var mismatch = await dispatcher.Client.GetAsync($"{path}?expectedVersion=none");
        mismatch.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await mismatch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("Operations.Flight.CustomerLogoChanged");

        await UploadLogoAsync(logoBytes);
        using var unchangedLogo = await dispatcher.Client.GetAsync($"{path}?expectedVersion={version}");
        unchangedLogo.StatusCode.ShouldBe(HttpStatusCode.OK);
        unchangedLogo.Headers.ETag!.Tag.ShouldBe(logo.Headers.ETag.Tag);

        var replacementBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        await UploadLogoAsync(replacementBytes);
        (await dispatcher.Client.GetAsync($"{path}?expectedVersion={version}")).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        var replacementOptions = await admin.GetFromJsonAsync<List<CustomerLogoOption>>($"{MasterDataBase}/customers/options");
        var replacementVersion = replacementOptions!.Single(customer => customer.Id == refs.CustomerId).LogoVersion;
        replacementVersion.ShouldNotBe(version);
        using var replacementLogo = await dispatcher.Client.GetAsync($"{path}?expectedVersion={replacementVersion}");
        replacementLogo.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await replacementLogo.Content.ReadAsByteArrayAsync()).ShouldBe(replacementBytes);
        replacementLogo.Headers.ETag!.Tag.ShouldBe($"\"{replacementVersion}\"");

        (await regular.Client.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await dispatcher.Client.GetAsync($"{Base}/flights/customer-logos/{Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await dispatcher.Client.GetAsync($"{MasterDataBase}/customers/{refs.CustomerId}/logo"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Mobile_routes_limit_categories_station_and_overlap_without_narrowing_portal_reads()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs,
            ["operations.flights.view", "operations.flights.view-station", "operations.flights.invite"]);
        using var client = staff.Client;
        var me = await client.GetFromJsonAsync<JsonElement>($"{IdentityBase}/me");
        var userId = me.GetProperty("id").GetGuid();
        var assignedId = await ScheduleFlightAsync(admin, refs.StationId, refs, "6100", [staff.StaffId]);
        var unassignedId = await ScheduleFlightAsync(admin, refs.StationId, refs, "6200");
        var now = DateTimeOffset.UtcNow;

        Flight SeedFlight(string number, bool perLanding = false, bool adHoc = false,
            Guid? creator = null, Guid? stationId = null, int arrivalHours = 0, int departureHours = 1) =>
            Flight.ScheduleNew(new CustomerSnapshot(refs.CustomerId, null, "Mobile customer"),
                new StationSnapshot(stationId ?? refs.StationId, "TST", "Mobile station"),
                new OperationTypeSnapshot(adHoc ? WellKnownMasterDataIds.AdHocOperationType : refs.OperationTypeId,
                    adHoc ? "Ad Hoc" : "Transit"), FlightNumber.Create(number).Value,
                ScheduledTime.Create(now.AddHours(arrivalHours), now.AddHours(departureHours)).Value,
                null, [new ServiceSnapshot(perLanding ? WellKnownMasterDataIds.AircraftPerLandingService : refs.ServiceId, "Service")],
                !perLanding && !adHoc ? [new StaffMemberSnapshot(staff.StaffId, "Staff", "E1")] : [],
                creator ?? userId, now).Value;

        var landing = SeedFlight("6300", perLanding: true);
        var ownAdHoc = SeedFlight("6400", adHoc: true);
        var ownMixed = SeedFlight("6500", perLanding: true, adHoc: true);
        var otherAdHoc = SeedFlight("6600", adHoc: true, creator: Guid.NewGuid());
        var otherMixed = SeedFlight("6700", perLanding: true, adHoc: true, creator: Guid.NewGuid());
        var spanning = SeedFlight("6800", arrivalHours: -20, departureHours: 1);
        var outdated = SeedFlight("6900", arrivalHours: -50, departureHours: -49);
        var otherStation = SeedFlight("6999", stationId: Guid.NewGuid());
        var future = SeedFlight("6998", arrivalHours: 13, departureHours: 14);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            db.Flights.AddRange(landing, ownAdHoc, ownMixed, otherAdHoc, otherMixed, spanning, outdated, otherStation, future);
            await db.SaveChangesAsync();
        }

        var mobile = $"{Base}/flights/mobile";
        var page = await client.GetFromJsonAsync<MobileFlightPageDto>($"{mobile}?pageSize=100");
        page.ShouldNotBeNull();
        page!.StationId.ShouldBe(refs.StationId);
        page.WindowStartUtc.ShouldBe(page.AsOfUtc.AddHours(-12));
        page.WindowEndUtc.ShouldBe(page.AsOfUtc.AddHours(12));
        page.Items.Select(f => f.Id).ShouldBe([assignedId, spanning.Id, landing.Id, ownAdHoc.Id, ownMixed.Id], ignoreOrder: true);
        page.Items.ShouldAllBe(f => f.StationId == refs.StationId);
        var mine = await client.GetFromJsonAsync<MobileFlightPageDto>($"{mobile}?category=MyFlights");
        mine!.Items.Select(f => f.Id).ShouldBe([assignedId, spanning.Id], ignoreOrder: true);
        var perLanding = await client.GetFromJsonAsync<MobileFlightPageDto>($"{mobile}?category=PerLanding");
        perLanding!.Items.ShouldHaveSingleItem().Id.ShouldBe(landing.Id);
        var adHoc = await client.GetFromJsonAsync<MobileFlightPageDto>($"{mobile}?category=AdHoc");
        adHoc!.Items.Select(f => f.Id).ShouldBe([ownAdHoc.Id, ownMixed.Id], ignoreOrder: true);
        adHoc.Items.ShouldAllBe(f => f.IsMyAdHocFlight && !f.IsMyFlight);
        (await client.GetAsync($"{mobile}?category=unknown")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetAsync(mobile)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"{mobile}/{spanning.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var id in new[] { outdated.Id, otherStation.Id, otherAdHoc.Id, otherMixed.Id, unassignedId, future.Id })
        {
            (await client.GetAsync($"{mobile}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.GetAsync($"{mobile}/{id}/invitation-options")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        // A caller cannot widen this surface by attaching the portal's date/station parameters.
        var ignoredFilters = await client.GetFromJsonAsync<MobileFlightPageDto>(
            $"{mobile}?pageSize=100&stationId={otherStation.Station.StationId}&fromUtc=2000-01-01&toUtc=2100-01-01");
        ignoredFilters!.Items.Select(f => f.Id).ShouldBe(page.Items.Select(f => f.Id), ignoreOrder: true);

        (await admin.GetAsync($"{Base}/flights/{outdated.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"{Base}/flights/{unassignedId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var targetIds = new[] { assignedId, unassignedId, landing.Id, ownAdHoc.Id, ownMixed.Id, otherAdHoc.Id,
            otherMixed.Id, spanning.Id, outdated.Id, otherStation.Id, future.Id, Guid.NewGuid(), Guid.Empty, landing.Id };
        using var targets = await client.PostAsJsonAsync($"{mobile}/notification-targets", new { flightIds = targetIds });
        targets.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await targets.Content.ReadFromJsonAsync<Guid[]>()).ShouldBe(page.Items.Select(item => item.Id), ignoreOrder: true);
        (await admin.PostAsJsonAsync($"{mobile}/notification-targets", new { flightIds = targetIds }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Notification_target_checks_require_flight_view_permission()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, []);
        using var client = staff.Client;
        foreach (var path in new[] { "flights/notification-targets", "flights/mobile/notification-targets" })
            (await client.PostAsJsonAsync($"{Base}/{path}", new { flightIds = new[] { Guid.NewGuid() } }))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Helpers ---------------------------------------------------------------

    private sealed record MasterDataRefs(
        Guid CountryId, Guid StationId, Guid CustomerId, Guid OperationTypeId, Guid ServiceId, Guid ManpowerTypeId);

    [Fact]
    public async Task Arrival_save_attributes_the_actor_replays_lost_success_and_locks_other_employees()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var first = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var second = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var firstClient = first.Client;
        using var secondClient = second.Client;
        var aircraftId = await PostForIdAsync(admin, $"{MasterDataBase}/aircraft-types",
            new { manufacturer = "Airbus", model = $"A-{Guid.NewGuid():N}" });
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "7100", [first.StaffId, second.StaffId]);
        var path = $"{Base}/flights/{id}/arrival";
        var detail = await firstClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}");
        detail!.CanSaveArrival.ShouldBeTrue();
        detail.CurrentStaffMemberId.ShouldBe(first.StaffId);
        var input = new SaveFlightArrivalRequest(DateTimeOffset.UtcNow.AddHours(-2), aircraftId, " HZ-TEST ", " REF-1 ", " Arrival remarks ");
        async Task<HttpResponseMessage> Save(HttpClient client, SaveFlightArrivalRequest payload, string version)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(payload) };
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            return await client.SendAsync(request);
        }
        using var initial = await Save(firstClient, input, detail.RowVersion);
        initial.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await initial.Content.ReadFromJsonAsync<FlightArrivalSavedDto>())!;
        saved.Submission.SubmittedByEmployee.StaffMemberId.ShouldBe(first.StaffId);
        saved.Submission.ReceivedByEmployee.StaffMemberId.ShouldBe(first.StaffId);
        saved.Submission.AircraftTailNumber.ShouldBe("HZ-TEST");
        saved.Submission.CustomerWorkReferenceNumber.ShouldBe("REF-1");
        saved.Submission.IsAccepted.ShouldBeTrue();
        saved.RowVersion.ShouldNotBe(detail.RowVersion);

        // Simulate a response lost after commit: exact original input and version are replayed.
        using var replay = await Save(firstClient, input, detail.RowVersion);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        var replayed = (await replay.Content.ReadFromJsonAsync<FlightArrivalSavedDto>())!;
        replayed.Submission.Id.ShouldBe(saved.Submission.Id);
        replayed.RowVersion.ShouldBe(saved.RowVersion);
        using var staleCorrection = await Save(firstClient, input with { AircraftTailNumber = "HZ-NEW" }, detail.RowVersion);
        staleCorrection.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var shared = (await secondClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        shared.ArrivalSubmissions!.ShouldHaveSingleItem().ShouldBe(saved.Submission);
        using var otherWrite = await Save(secondClient, input, shared.RowVersion);
        otherWrite.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var afterOther = (await firstClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        afterOther.RowVersion.ShouldBe(saved.RowVersion);
        afterOther.AircraftTypeId.ShouldBeNull(); // Actual aircraft never replaces the plan.

        using var correction = await Save(firstClient, input with { AircraftTailNumber = "HZ-NEW" }, saved.RowVersion);
        correction.StatusCode.ShouldBe(HttpStatusCode.OK);
        var corrected = (await correction.Content.ReadFromJsonAsync<FlightArrivalSavedDto>())!;
        corrected.Submission.Id.ShouldBe(saved.Submission.Id);
        corrected.Submission.AircraftTailNumber.ShouldBe("HZ-NEW");
        corrected.Submission.UpdatedAtUtc.ShouldNotBeNull();

        var concurrentId = await ScheduleFlightAsync(admin, refs.StationId, refs, "7101", [first.StaffId, second.StaffId]);
        var concurrent = (await firstClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{concurrentId}"))!;
        async Task<HttpStatusCode> Race(HttpClient client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{concurrentId}/arrival") { Content = JsonContent.Create(input) };
            request.Headers.TryAddWithoutValidation("If-Match", concurrent.RowVersion);
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }
        var outcomes = await Task.WhenAll(Race(firstClient), Race(secondClient));
        outcomes.Count(status => status == HttpStatusCode.OK).ShouldBe(1);
        outcomes.ShouldAllBe(status => status == HttpStatusCode.OK || status == HttpStatusCode.Conflict || status == HttpStatusCode.Forbidden);
        var afterRace = (await firstClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{concurrentId}"))!;
        afterRace.ArrivalSubmissions!.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Arrival_boundary_rejects_missing_fields_and_permissions_without_writing()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var reader = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        using var staffClient = staff.Client;
        using var readClient = reader.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "7200", [staff.StaffId, reader.StaffId]);
        var detail = (await staffClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        var path = $"{Base}/flights/{id}/arrival";
        var input = new SaveFlightArrivalRequest(DateTimeOffset.UtcNow, Guid.NewGuid(), "HZ-TEST", null, null);
        using var missingVersion = await staffClient.PutAsJsonAsync(path, input);
        missingVersion.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missingVersion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("General.PreconditionRequired");
        foreach (var invalid in new[] { input with { Ata = default }, input with { AircraftTypeId = Guid.Empty }, input with { AircraftTailNumber = "  " } })
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(invalid) };
            request.Headers.TryAddWithoutValidation("If-Match", detail.RowVersion);
            using var response = await staffClient.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        (await readClient.PutAsJsonAsync(path, input)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var readDetail = (await readClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        readDetail.CanSaveArrival.ShouldBeFalse();
        readDetail.ArrivalSubmissions!.ShouldBeEmpty();
        readDetail.RowVersion.ShouldBe(detail.RowVersion);
        var hiddenId = await ScheduleFlightAsync(admin, refs.StationId, refs, "7201");
        using var hidden = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{hiddenId}/arrival") { Content = JsonContent.Create(input) };
        hidden.Headers.TryAddWithoutValidation("If-Match", detail.RowVersion);
        (await staffClient.SendAsync(hidden)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Departure_save_persists_real_signature_bytes_shares_attribution_and_replays_lost_success()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var first = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var second = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var firstClient = first.Client;
        using var secondClient = second.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "7300", [first.StaffId, second.StaffId]);
        var path = $"{Base}/flights/{id}/departure";
        var detail = (await firstClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        detail.CanSaveDeparture.ShouldBeTrue();
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");
        var input = new SaveFlightDepartureRequest(DateTimeOffset.UtcNow.AddHours(2), png,
            DateTimeOffset.UtcNow.AddMinutes(-1), " Departure remarks ");
        async Task<HttpResponseMessage> Save(HttpClient client, SaveFlightDepartureRequest payload, string version)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(payload) };
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            return await client.SendAsync(request);
        }
        using var initial = await Save(firstClient, input, detail.RowVersion);
        initial.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await initial.Content.ReadFromJsonAsync<FlightDepartureSavedDto>())!;
        saved.Submission.SubmittedByEmployee.StaffMemberId.ShouldBe(first.StaffId);
        saved.Submission.HandledByEmployee.StaffMemberId.ShouldBe(first.StaffId);
        saved.Submission.Remarks.ShouldBe("Departure remarks");
        saved.Submission.IsAccepted.ShouldBeTrue();
        saved.RowVersion.ShouldNotBe(detail.RowVersion);
        string PreviewUrl(FlightDepartureSubmissionDto submission, Guid? flightId = null, Guid? submissionId = null) =>
            $"{Base}/flights/mobile/{flightId ?? id}/departure-signatures/{submissionId ?? submission.Id}" +
            $"?expectedUpdatedAt={Uri.EscapeDataString((submission.UpdatedAtUtc ?? submission.CreatedAtUtc).ToString("O"))}";
        foreach (var client in new[] { firstClient, secondClient })
        {
            using var preview = await client.GetAsync(PreviewUrl(saved.Submission));
            preview.StatusCode.ShouldBe(HttpStatusCode.OK);
            preview.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
            preview.Headers.CacheControl!.Private.ShouldBeTrue();
            preview.Headers.CacheControl.NoStore.ShouldBeTrue();
            (await preview.Content.ReadAsByteArrayAsync()).ShouldBe(png);
        }
        using (var wrongSubmission = await firstClient.GetAsync(PreviewUrl(saved.Submission, submissionId: Guid.NewGuid())))
            wrongSubmission.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var hiddenId = await ScheduleFlightAsync(admin, refs.StationId, refs, "7301");
        using (var hiddenPreview = await firstClient.GetAsync(PreviewUrl(saved.Submission, flightId: hiddenId)))
            hiddenPreview.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using (var anonymous = factory.CreateClient())
        using (var unauthenticated = await anonymous.GetAsync(PreviewUrl(saved.Submission)))
            unauthenticated.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        saved.Submission.SignatureVersion.ShouldNotBeNullOrWhiteSpace();
        string originalKey;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var stored = (await db.Flights.Include(f => f.DepartureSubmissions).SingleAsync(f => f.Id == id))
                .DepartureSubmissions.Single().Details.CustomerSignature;
            originalKey = stored.StorageKey;
            stored.SizeBytes.ShouldBe(png.Length);
            stored.ContentType.ShouldBe("image/png");
            stored.SignedAtUtc.ShouldBe(input.SignatureSignedAtUtc!.Value);
            await using var content = await scope.ServiceProvider.GetRequiredService<IFileStorage>().OpenAsync(stored.StorageKey);
            content.ShouldNotBeNull();
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer);
            buffer.ToArray().ShouldBe(png);
        }
        using var replay = await Save(firstClient, input, detail.RowVersion);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        var replayed = (await replay.Content.ReadFromJsonAsync<FlightDepartureSavedDto>())!;
        replayed.Submission.Id.ShouldBe(saved.Submission.Id);
        replayed.RowVersion.ShouldBe(saved.RowVersion);
        using var staleCorrection = await Save(firstClient, input with { Atd = input.Atd.AddMinutes(10) }, detail.RowVersion);
        staleCorrection.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var shared = (await secondClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        shared.DepartureSubmissions!.ShouldHaveSingleItem().ShouldBe(saved.Submission);
        using var otherWrite = await Save(secondClient, input, shared.RowVersion);
        otherWrite.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // The owner's ATD/remarks correction reuses the saved signature instead of requiring a redraw.
        using var correction = await Save(firstClient, input with {
            Atd = input.Atd.AddMinutes(10), SignatureContent = null, SignatureSignedAtUtc = null, Remarks = "Corrected"
        }, saved.RowVersion);
        correction.StatusCode.ShouldBe(HttpStatusCode.OK);
        var corrected = (await correction.Content.ReadFromJsonAsync<FlightDepartureSavedDto>())!;
        corrected.Submission.Id.ShouldBe(saved.Submission.Id);
        corrected.Submission.Remarks.ShouldBe("Corrected");
        corrected.Submission.UpdatedAtUtc.ShouldNotBeNull();
        corrected.Submission.SignatureVersion.ShouldBe(saved.Submission.SignatureVersion);
        using (var stalePreview = await firstClient.GetAsync(PreviewUrl(saved.Submission)))
            stalePreview.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        using (var correctedPreview = await firstClient.GetAsync(PreviewUrl(corrected.Submission)))
        {
            correctedPreview.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await correctedPreview.Content.ReadAsByteArrayAsync()).ShouldBe(png);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var stored = (await db.Flights.Include(f => f.DepartureSubmissions).SingleAsync(f => f.Id == id))
                .DepartureSubmissions.Single().Details.CustomerSignature;
            stored.StorageKey.ShouldBe(originalKey);
        }
        // Re-signing stores a new file and releases only an unreferenced previous file.
        using var resign = await Save(firstClient, input with { SignatureSignedAtUtc = input.SignatureSignedAtUtc!.Value.AddSeconds(1) }, corrected.RowVersion);
        resign.StatusCode.ShouldBe(HttpStatusCode.OK);
        var resigned = (await resign.Content.ReadFromJsonAsync<FlightDepartureSavedDto>())!;
        resigned.Submission.SignatureVersion.ShouldNotBe(corrected.Submission.SignatureVersion);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var stored = (await db.Flights.Include(f => f.DepartureSubmissions).SingleAsync(f => f.Id == id))
                .DepartureSubmissions.Single().Details.CustomerSignature;
            stored.StorageKey.ShouldNotBe(originalKey);
            await using var oldContent = await scope.ServiceProvider.GetRequiredService<IFileStorage>().OpenAsync(originalKey);
            oldContent.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Departure_boundary_requires_permission_precondition_atd_and_a_real_signed_png()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var reader = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        using var actorClient = actor.Client;
        using var readerClient = reader.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "7400", [actor.StaffId, reader.StaffId]);
        var path = $"{Base}/flights/{id}/departure";
        var detail = (await actorClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");
        var input = new SaveFlightDepartureRequest(DateTimeOffset.UtcNow, png, DateTimeOffset.UtcNow, null);
        (await actorClient.PutAsJsonAsync(path, input)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await readerClient.PutAsJsonAsync(path, input)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var anonymous = factory.CreateClient();
        (await anonymous.PutAsJsonAsync(path, input)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        foreach (var invalid in new[] {
            input with { Atd = default },
            input with { SignatureContent = null, SignatureSignedAtUtc = null },
            input with { SignatureContent = [] },
            input with { SignatureContent = [137, 80, 78, 71] },
            input with { SignatureContent = png[..^1] },
            input with { SignatureContent = new byte[2 * 1024 * 1024 + 1] },
            input with { SignatureSignedAtUtc = null },
            input with { SignatureSignedAtUtc = default(DateTimeOffset) }
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(invalid) };
            request.Headers.TryAddWithoutValidation("If-Match", detail.RowVersion);
            (await actorClient.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        var unchanged = (await actorClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        unchanged.DepartureSubmissions!.ShouldBeEmpty();
        unchanged.RowVersion.ShouldBe(detail.RowVersion);
        var readDetail = (await readerClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        readDetail.CanSaveDeparture.ShouldBeFalse();
        var hiddenId = await ScheduleFlightAsync(admin, refs.StationId, refs, "7401");
        using var hidden = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{hiddenId}/departure") { Content = JsonContent.Create(input) };
        hidden.Headers.TryAddWithoutValidation("If-Match", detail.RowVersion);
        (await actorClient.SendAsync(hidden)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<MasterDataRefs> SetupMasterDataAsync(HttpClient admin)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var countries = await admin.GetFromJsonAsync<PagedList<CountryItem>>($"{MasterDataBase}/countries?page=1&pageSize=1");
        countries!.Items.ShouldNotBeEmpty();
        var countryId = countries.Items[0].Id;

        var stationId = await CreateStationAsync(admin, countryId);
        var customerId = await PostForIdAsync(admin, $"{MasterDataBase}/customers", new
        {
            iataCode = (string?)null,
            icaoCode = (string?)null,
            name = $"Customer {suffix}",
            countryId,
            officialEmail = (string?)null,
            officialPhone = (string?)null,
            address = new { line1 = "1 Airport Rd", line2 = (string?)null, city = "City", region = (string?)null, postalCode = (string?)null },
            contacts = Array.Empty<object>()
        });
        var operationTypeId = await PostForIdAsync(admin, $"{MasterDataBase}/operation-types",
            new { name = $"Transit {suffix}", description = (string?)null });
        var serviceId = await PostForIdAsync(admin, $"{MasterDataBase}/services",
            new { name = $"Marshalling {suffix}", description = (string?)null });
        var manpowerTypeId = await PostForIdAsync(admin, $"{MasterDataBase}/manpower-types",
            new { name = $"Manpower {suffix}", description = (string?)null });
        await AllowServiceAsync(admin, manpowerTypeId, serviceId);

        return new MasterDataRefs(countryId, stationId, customerId, operationTypeId, serviceId, manpowerTypeId);
    }

    private static async Task AllowServiceAsync(HttpClient admin, Guid manpowerTypeId, Guid serviceId)
    {
        var detail = await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/manpower-types/{manpowerTypeId}");
        var request = new HttpRequestMessage(HttpMethod.Put, $"{MasterDataBase}/manpower-types/{manpowerTypeId}/service-allowances")
        {
            Content = JsonContent.Create(new { serviceIds = new[] { serviceId } })
        };
        request.Headers.TryAddWithoutValidation("If-Match", detail!.RowVersion);
        (await admin.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private sealed record ConcurrencyDetail(string RowVersion);

    private static async Task<Guid> CreateStationAsync(HttpClient admin, Guid countryId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return await PostForIdAsync(admin, $"{MasterDataBase}/stations",
            new { iataCode = NextThreeLetterCode(), icaoCode = (string?)null, name = $"Station {suffix}", city = "City", countryId, timeZoneId = "Etc/UTC" });
    }

    /// <summary>Creates a staff member at the refs station with a role holding <paramref name="permissions"/> and returns a logged-in client.</summary>
    private async Task<(HttpClient Client, Guid StaffId)> CreateStaffLoginAsync(HttpClient admin, MasterDataRefs refs, string[] permissions)
    {
        var roleId = await PostForIdAsync(admin, $"{IdentityBase}/roles", new
        {
            name = $"Visibility Role {Guid.NewGuid():N}",
            description = (string?)null,
            compatibleUserType = "StationStaff",
            permissions
        });

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"visibility-staff-{suffix}@example.com";
        var staffId = await PostForIdAsync(admin, $"{MasterDataBase}/staff-members", new
        {
            fullName = $"Visibility Staff {suffix}",
            employeeId = $"EMP-{suffix}",
            email,
            stationId = refs.StationId,
            manpowerTypeId = refs.ManpowerTypeId,
            employmentContract = (object?)null,
            workingDays = (string[]?)null,
            licenses = Array.Empty<object>(),
            portalAccessRoleId = roleId
        });

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull($"expected an invitation email for {email}");

        const string password = "StaffPass#12345";
        (await admin.PostAsJsonAsync($"{IdentityBase}/auth/activate",
            new { email, invitationToken, newPassword = password }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var client = await factory.CreateAuthenticatedClientAsync(email, password);
        return (client, staffId);
    }

    private static async Task<Guid> ScheduleFlightAsync(
        HttpClient client, Guid stationId, MasterDataRefs refs, string flightNumber,
        IReadOnlyList<Guid>? assignedStaffIds = null)
    {
        var response = await client.PostAsJsonAsync($"{Base}/flights", new
        {
            customerId = refs.CustomerId,
            stationId,
            operationTypeId = refs.OperationTypeId,
            flightNumber,
            scheduledArrivalUtc = DateTimeOffset.UtcNow.AddHours(2),
            scheduledDepartureUtc = DateTimeOffset.UtcNow.AddHours(4),
            aircraftTypeId = (Guid?)null,
            plannedServiceIds = new[] { refs.ServiceId },
            assignedStaffMemberIds = assignedStaffIds ?? []
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> PostForIdAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, $"POST {path} failed: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    // Unique IATA generator with a prefix distinct from other test classes in this assembly.
    private static string NextThreeLetterCode()
    {
        var n = Interlocked.Increment(ref _stationCounter);
        return $"V{(char)('A' + (n / 26) % 26)}{(char)('A' + n % 26)}";
    }

    // --- Response mirrors -------------------------------------------------------

    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);

    private sealed record CountryItem(Guid Id, string Name, string IsoCode, bool IsActive);

    private sealed record FlightListItem(Guid Id, string FlightNumber, bool IsPerLanding,
        Guid? CustomerId = null, string? CustomerLogoVersion = null);
    private sealed record CustomerLogoOption(Guid Id, string? LogoVersion);

}
