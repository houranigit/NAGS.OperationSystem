using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using Operations.Application.Contracts;
using PdfSharp.Pdf.IO;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task Work_order_email_captures_each_saved_revision_for_the_opted_in_actor()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        var peer = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        using var actorClient = actor.Client;
        using var peerClient = peer.Client;
        await SetWorkOrderEmailPreferenceAsync(actorClient, enabled: true);
        await SetWorkOrderEmailPreferenceAsync(peerClient, enabled: true);
        var profile = await actorClient.GetFromJsonAsync<JsonElement>($"{IdentityBase}/me");
        var recipient = profile.GetProperty("email").GetString();

        var arrivalTime = DateTimeOffset.UtcNow.AddHours(-2);
        var flightId = await SchedulePortalFlightAtAsync(admin, refs, "8796", arrivalTime, [actor.StaffId, peer.StaffId]);
        var detail = await PortalOperationsDetailAsync(actorClient, flightId);
        var subject = $"Work order updated — {detail.FlightNumber}";
        EmailMessage[] Receipts() => factory.Emails.Messages.Where(message => message.Subject == subject).ToArray();
        var printPath = $"{Base}/flights/{flightId}/operations/print";
        var aircraft = await OperationsBatchAircraftAsync(admin);
        var departureTime = arrivalTime.AddMinutes(45);
        var batch = OperationsBatchInput(
            new SaveFlightArrivalRequest(arrivalTime, aircraft, "HZ-MAIL", "MAIL-REF", "Initial arrival"),
            new SaveFlightDepartureRequest(departureTime, ServicePng(), departureTime, "Departure complete"),
            [new(Guid.NewGuid(), OperationsBatchClosedService(refs.ServiceId, actor.StaffId,
                arrivalTime.AddMinutes(5), arrivalTime.AddMinutes(20)))]);

        using var saved = await WritePortalOperationsAsync(actorClient, flightId, HttpMethod.Put, "batch", batch, detail.RowVersion);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var firstRevision = (await saved.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        using var retry = await WritePortalOperationsAsync(actorClient, flightId, HttpMethod.Put, "batch", batch, detail.RowVersion);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        (await retry.Content.ReadFromJsonAsync<FlightDetailDto>())!.RowVersion.ShouldBe(firstRevision.RowVersion);
        var initialPdf = await ReadPrintedWorkOrderAsync(actorClient, printPath);

        var occurrenceId = Guid.NewGuid();
        var returnArrival = departureTime.AddMinutes(9);
        var returnInput = new SaveFlightReturnToRampRequest(
            new(returnArrival, "Returned to stand"),
            new(returnArrival.AddMinutes(30), "Released from stand"),
            [new(Guid.NewGuid(), OperationsBatchClosedService(refs.ServiceId, actor.StaffId,
                returnArrival.AddMinutes(5), returnArrival.AddMinutes(20)))], []);
        using var mobileReturn = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{flightId}/returns-to-ramp/{occurrenceId}")
        {
            Content = JsonContent.Create(returnInput)
        };
        mobileReturn.Headers.TryAddWithoutValidation("If-Match", firstRevision.RowVersion);
        using var returned = await actorClient.SendAsync(mobileReturn);
        returned.StatusCode.ShouldBe(HttpStatusCode.OK, await returned.Content.ReadAsStringAsync());
        var secondRevision = (await returned.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        var returnedPdf = await ReadPrintedWorkOrderAsync(actorClient, printPath);
        returnedPdf.Pages.Count.ShouldBeGreaterThan(initialPdf.Pages.Count);
        Receipts().ShouldBeEmpty();

        await factory.DrainOutboxesAsync();
        var receipts = Receipts();
        receipts.Length.ShouldBe(2);
        receipts.ShouldAllBe(receipt => receipt.ToEmail == recipient);
        AssertWorkOrderReceipt(receipts[0], initialPdf);
        AssertWorkOrderReceipt(receipts[1], returnedPdf);

        await SetWorkOrderEmailPreferenceAsync(actorClient, enabled: false);
        var correction = returnInput with { Arrival = returnInput.Arrival with { Remarks = "Updated return remarks" } };
        using var updated = await WritePortalOperationsAsync(actorClient, flightId, HttpMethod.Put,
            $"returns-to-ramp/{occurrenceId}", correction, secondRevision.RowVersion);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        (await updated.Content.ReadFromJsonAsync<FlightDetailDto>())!.RowVersion.ShouldNotBe(secondRevision.RowVersion);
        await factory.DrainOutboxesAsync();
        Receipts().Length.ShouldBe(2);
    }

    [Fact]
    public async Task Work_order_email_requires_the_same_read_permission_as_printing()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var operateOnly = await CreateStaffLoginAsync(admin, refs, ["operations.flights.operate"]);
        using var client = operateOnly.Client;
        await SetWorkOrderEmailPreferenceAsync(client, enabled: true);

        var arrivalTime = DateTimeOffset.UtcNow.AddHours(-2);
        var flightId = await SchedulePortalFlightAtAsync(admin, refs, "8795", arrivalTime, [operateOnly.StaffId]);
        var flight = await PortalOperationsDetailAsync(admin, flightId);
        var aircraft = await OperationsBatchAircraftAsync(admin);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{flightId}/arrival")
        {
            Content = JsonContent.Create(new SaveFlightArrivalRequest(arrivalTime, aircraft, "HZ-MAIL2", null, "Arrival recorded"))
        };
        request.Headers.TryAddWithoutValidation("If-Match", flight.RowVersion);
        using var saved = await client.SendAsync(request);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        using var print = await client.GetAsync($"{Base}/flights/mobile/{flightId}/print");
        print.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await factory.DrainOutboxesAsync();
        factory.Emails.Messages.ShouldNotContain(message => message.Subject == $"Work order updated — {flight.FlightNumber}");
    }

    private static async Task SetWorkOrderEmailPreferenceAsync(HttpClient client, bool enabled)
    {
        using var response = await client.PutAsJsonAsync($"{IdentityBase}/me/work-order-email-preference", new { enabled });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static async Task<PrintedWorkOrder> ReadPrintedWorkOrderAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return ReadWorkOrderPages(response.Content.Headers.ContentDisposition!.FileNameStar!, await response.Content.ReadAsByteArrayAsync());
    }

    private static void AssertWorkOrderReceipt(EmailMessage receipt, PrintedWorkOrder expected)
    {
        var attachment = receipt.Attachments!.ShouldHaveSingleItem();
        attachment.ContentType.ShouldBe("application/pdf");
        attachment.FileName.ShouldBe(expected.FileName);
        var attached = ReadWorkOrderPages(attachment.FileName, attachment.Content);
        attached.Pages.Count.ShouldBe(expected.Pages.Count);
        for (var index = 0; index < expected.Pages.Count; index++)
            attached.Pages[index].ShouldBe(expected.Pages[index]);
    }

    private static PrintedWorkOrder ReadWorkOrderPages(string fileName, byte[] bytes)
    {
        using var document = PdfReader.Open(new MemoryStream(bytes));
        // Compare the rendered pages without the per-file PDF metadata and identifiers.
        var pages = new byte[document.PageCount][];
        for (var index = 0; index < pages.Length; index++)
            pages[index] = document.Pages[index].Contents.CreateSingleContent().Stream!.UnfilteredValue;
        return new(fileName, pages);
    }

    private sealed record PrintedWorkOrder(string FileName, IReadOnlyList<byte[]> Pages);
}
