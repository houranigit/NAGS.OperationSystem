using System.Net;
using System.Net.Http.Json;
using System.Text;
using Operations.Application.Contracts;
using PdfSharp.Pdf.IO;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task Work_order_print_uses_portal_and_mobile_read_scopes_before_and_after_work_is_recorded()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var viewer = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        var operatorAccount = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        using var viewerClient = viewer.Client;
        using var operatorClient = operatorAccount.Client;
        var arrivalTime = DateTimeOffset.UtcNow.AddHours(-2);
        var flight = await SchedulePortalFlightAtAsync(admin, refs, "8799", arrivalTime, [viewer.StaffId, operatorAccount.StaffId]);
        var otherFlight = await SchedulePortalFlightAtAsync(admin, refs, "8798", arrivalTime, [operatorAccount.StaffId]);
        var outsideWindow = await SchedulePortalFlightAtAsync(admin, refs, "8797", arrivalTime.AddHours(25), [viewer.StaffId]);
        var path = $"{Base}/flights/{flight}/operations/print";
        var mobilePath = $"{Base}/flights/mobile/{flight}/print";

        await AssertWorkOrderPdfAsync(viewerClient, path, "8799", expectedMinPages: 1);
        await AssertWorkOrderPdfAsync(viewerClient, mobilePath, "8799", expectedMinPages: 1);
        using var hidden = await viewerClient.GetAsync($"{Base}/flights/{otherFlight}/operations/print");
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await AssertWorkOrderPdfAsync(viewerClient, $"{Base}/flights/mobile/{otherFlight}/print", "8798", expectedMinPages: 1);
        using var notYetVisible = await viewerClient.GetAsync($"{Base}/flights/mobile/{outsideWindow}/print");
        notYetVisible.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var missingStaffLink = await admin.GetAsync(mobilePath);
        missingStaffLink.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var detail = await PortalOperationsDetailAsync(operatorClient, flight);
        var aircraft = await OperationsBatchAircraftAsync(admin);
        var arrival = new SaveFlightArrivalRequest(arrivalTime, aircraft, "HZ-PRINT", "PRINT-REF", "Arrival on stand");
        var normalWork = OperationsBatchClosedService(refs.ServiceId, operatorAccount.StaffId,
            arrivalTime.AddMinutes(5), arrivalTime.AddMinutes(20));
        var departureTime = arrivalTime.AddMinutes(45);
        using var saved = await WritePortalOperationsAsync(operatorClient, flight, HttpMethod.Put, "batch",
            OperationsBatchInput(arrival, new SaveFlightDepartureRequest(departureTime, ServicePng(), departureTime, "Departure complete"),
                [new(Guid.NewGuid(), normalWork)]), detail.RowVersion);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var savedDetail = (await saved.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        var returnArrival = departureTime.AddMinutes(9);
        var returnWork = OperationsBatchClosedService(refs.ServiceId, operatorAccount.StaffId,
            returnArrival.AddMinutes(5), returnArrival.AddMinutes(20));
        var returnInput = new SaveFlightReturnToRampRequest(
            new(returnArrival, "Returned to stand"),
            new(returnArrival.AddMinutes(30), "Released from stand"),
            [new(Guid.NewGuid(), returnWork)], []);
        using var returned = await WritePortalOperationsAsync(operatorClient, flight, HttpMethod.Put,
            $"returns-to-ramp/{Guid.NewGuid()}", returnInput, savedDetail.RowVersion);
        returned.StatusCode.ShouldBe(HttpStatusCode.OK, await returned.Content.ReadAsStringAsync());
        var afterReturn = (await returned.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        afterReturn.WorkOrderNumber.ShouldBeNull();
        await AssertWorkOrderPdfAsync(viewerClient, path, "8799", expectedMinPages: 2);
        await AssertWorkOrderPdfAsync(viewerClient, mobilePath, "8799", expectedMinPages: 2);
        (await PortalOperationsDetailAsync(operatorClient, flight)).RowVersion.ShouldBe(afterReturn.RowVersion);
    }

    private static async Task AssertWorkOrderPdfAsync(HttpClient client, string path, string flightNumber, int expectedMinPages)
    {
        using var response = await client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/pdf");
        response.Content.Headers.ContentDisposition?.DispositionType.ShouldBe("inline");
        response.Content.Headers.ContentDisposition?.FileNameStar.ShouldEndWith($"-{flightNumber}.pdf");
        response.Headers.CacheControl?.Private.ShouldBeTrue();
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        var content = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(content, 0, 5).ShouldBe("%PDF-");
        using var file = PdfReader.Open(new MemoryStream(content));
        file.Info.Title.ShouldContain(flightNumber);
        file.PageCount.ShouldBeGreaterThanOrEqualTo(expectedMinPages);
    }
}
