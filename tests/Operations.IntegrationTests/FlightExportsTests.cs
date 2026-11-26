using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Operations.Application.Contracts;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task Flight_exports_preserve_filters_sort_and_visibility_across_all_pages()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var exporter = await CreateStaffLoginAsync(admin, refs, [.. DispatcherPermissions, "operations.flights.export"]);
        var noExport = await CreateStaffLoginAsync(admin, refs, DispatcherPermissions);
        using var exportClient = exporter.Client;
        using var noExportClient = noExport.Client;

        await ScheduleFlightAsync(admin, refs.StationId, refs, "9301");
        await ScheduleFlightAsync(admin, refs.StationId, refs, "9302");
        await ScheduleFlightAsync(admin, refs.StationId, refs, "9303", perLanding: true);
        await ScheduleFlightAsync(admin, await CreateStationAsync(admin, refs.CountryId), refs, "9304");

        var query = $"customerId={refs.CustomerId}&search=930&flightType=Scheduled&status=Scheduled&sort=flightNumber%20desc&page=1&pageSize=1";
        var page = await exportClient.GetFromJsonAsync<PagedList<FlightListItemDto>>($"{Base}/flights?{query}");
        page!.TotalCount.ShouldBe(2);
        page.Items.Select(item => item.FlightNumber).ShouldBe(["9302"]);

        using var denied = await noExportClient.GetAsync($"{Base}/flights/export?format=csv&{query}");
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var csv = await exportClient.GetAsync($"{Base}/flights/export?format=csv&{query}");
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        csv.Content.Headers.ContentDisposition!.FileNameStar!.ShouldStartWith("flights-report-");
        var lines = (await csv.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldContain("WO#,Flight#,WO Flight#,STA,STD");
        lines.Skip(1).Select(line => line.Split(',')[2].Trim('"')).ShouldBe(["9302", "9301"]);

        using var xlsx = await exportClient.GetAsync($"{Base}/flights/export?format=xlsx&{query}");
        xlsx.StatusCode.ShouldBe(HttpStatusCode.OK);
        xlsx.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        using var workbook = new XLWorkbook(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync()));
        var flightSheet = workbook.Worksheet("Flights");
        flightSheet.Cell(5, 3).GetString().ShouldBe("Flight#");
        flightSheet.Cell(6, 3).GetString().ShouldBe("9302");
        flightSheet.Cell(7, 3).GetString().ShouldBe("9301");

        using var pdf = await exportClient.GetAsync($"{Base}/flights/export?format=pdf&{query}");
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await pdf.Content.ReadAsByteArrayAsync()).AsSpan().StartsWith("%PDF-"u8).ShouldBeTrue();
        using var invalid = await exportClient.GetAsync($"{Base}/flights/export?format=csv&report=billing&{query}");
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Flight_billing_export_bands_performed_services_by_flight_occurrence()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var operatorAccount = await CreateStaffLoginAsync(admin, refs, PortalOperatorPermissions);
        using var operatorClient = operatorAccount.Client;
        var arrival = DateTimeOffset.UtcNow.AddHours(-2);
        var aircraft = await OperationsBatchAircraftAsync(admin);
        foreach (var scheduledArrival in new[] { arrival, arrival.AddDays(-1) })
        {
            var id = await SchedulePortalFlightAtAsync(admin, refs, "9305", scheduledArrival, [operatorAccount.StaffId]);
            var flight = await PortalOperationsDetailAsync(operatorClient, id);
            var service = OperationsBatchClosedService(refs.ServiceId, operatorAccount.StaffId,
                scheduledArrival.AddMinutes(5), scheduledArrival.AddMinutes(25));
            using var saved = await WritePortalOperationsAsync(operatorClient, id, HttpMethod.Put, "batch",
                OperationsBatchInput(new(scheduledArrival, aircraft, "HZ-EXPORT", null, null),
                    new(scheduledArrival.AddHours(1), null, null, null), [new(Guid.NewGuid(), service)]), flight.RowVersion);
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var billing = await admin.GetAsync($"{Base}/flights/export?format=xlsx&report=billing&customerId={refs.CustomerId}");
        billing.StatusCode.ShouldBe(HttpStatusCode.OK);
        billing.Content.Headers.ContentDisposition!.FileNameStar!.ShouldStartWith("billing-report-");
        using var workbook = new XLWorkbook(new MemoryStream(await billing.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheet("Billing Report");
        sheet.Cell(1, 15).GetString().ShouldBe("Area");
        sheet.Cell(2, 6).GetString().ShouldBe("9305");
        sheet.Cell(2, 15).GetString().ShouldBe("Service");
        sheet.Cell(2, 16).GetString().ShouldStartWith("Marshalling");
        sheet.Cell(4, 6).GetString().ShouldBe("9305");
        sheet.Cell(4, 7).GetDateTime().ShouldNotBe(sheet.Cell(2, 7).GetDateTime());
        sheet.LastRowUsed()!.RowNumber().ShouldBe(5);
        var firstFill = sheet.Cell(2, 1).Style.Fill.BackgroundColor;
        var secondFill = sheet.Cell(4, 1).Style.Fill.BackgroundColor;
        firstFill.ShouldNotBe(secondFill);
        sheet.Range(2, 1, 3, 20).Cells().ShouldAllBe(cell => cell.Style.Fill.BackgroundColor == firstFill);
        sheet.Range(4, 1, 5, 20).Cells().ShouldAllBe(cell => cell.Style.Fill.BackgroundColor == secondFill);
    }
}
