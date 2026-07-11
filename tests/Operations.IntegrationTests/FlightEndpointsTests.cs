using System.Net;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class FlightEndpointsTests(OperationsApiFactory factory) : IClassFixture<OperationsApiFactory>
{
    [Fact]
    public async Task GetFlights_WithoutAuthentication_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync($"{OperationsApiFactory.Base}/flights");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExportFlights_WithoutAuthentication_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"{OperationsApiFactory.Base}/flights/export?format=xlsx");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx")]
    [InlineData("csv", "text/csv", ".csv")]
    [InlineData("pdf", "application/pdf", ".pdf")]
    public async Task ExportFlights_AsAdmin_ReturnsNativeAttachment(
        string format,
        string expectedMediaType,
        string expectedExtension)
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        await SeedFlightAsync();

        var response = await admin.GetAsync($"{OperationsApiFactory.Base}/flights/export?format={format}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull();
        response.Content.Headers.ContentType!.MediaType.ShouldBe(expectedMediaType);

        var disposition = response.Content.Headers.ContentDisposition;
        disposition.ShouldNotBeNull();
        disposition!.DispositionType.ShouldBe("attachment");

        var fileName = (disposition.FileNameStar ?? disposition.FileName)?.Trim('"');
        fileName.ShouldNotBeNullOrWhiteSpace();
        fileName!.ShouldStartWith("flights-report-");
        fileName.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase).ShouldBeTrue();

        var content = await response.Content.ReadAsByteArrayAsync();
        content.ShouldNotBeEmpty();

        switch (format)
        {
            case "xlsx":
                content.Length.ShouldBeGreaterThanOrEqualTo(2);
                content[0].ShouldBe((byte)'P');
                content[1].ShouldBe((byte)'K');

                using (var stream = new MemoryStream(content))
                using (var workbook = new XLWorkbook(stream))
                {
                    var sheet = workbook.Worksheet("Flights");
                    var lastRow = sheet.LastRowUsed()!.RowNumber();

                    for (var row = 6; row <= lastRow; row++)
                    {
                        var statusCell = sheet.Cell(row, 12);
                        var expectedColors = statusCell.GetString() switch
                        {
                            "Completed" => (Background: "#DDF7E7", Foreground: "#18733A"),
                            "Canceled" => (Background: "#FDE5E7", Foreground: "#A32632"),
                            "In progress" => (Background: "#FFF2C7", Foreground: "#9A5800"),
                            _ => (Background: "#E9EEF5", Foreground: "#536274")
                        };

                        statusCell.Style.Fill.BackgroundColor.Color.ToArgb()
                            .ShouldBe(XLColor.FromHtml(expectedColors.Background).Color.ToArgb());
                        statusCell.Style.Font.FontColor.Color.ToArgb()
                            .ShouldBe(XLColor.FromHtml(expectedColors.Foreground).Color.ToArgb());
                    }
                }
                break;
            case "csv":
                response.Content.Headers.ContentType.CharSet.ShouldBe("utf-8");
                content.Length.ShouldBeGreaterThanOrEqualTo(3);
                content[0].ShouldBe((byte)0xEF);
                content[1].ShouldBe((byte)0xBB);
                content[2].ShouldBe((byte)0xBF);

                var csv = Encoding.UTF8.GetString(content, 3, content.Length - 3);
                var header = csv.Split('\n', 2)[0].TrimEnd('\r');
                header.ShouldContain("Flight Number");
                csv.ShouldContain("Export Customer");
                break;
            case "pdf":
                content.Length.ShouldBeGreaterThanOrEqualTo(4);
                Encoding.ASCII.GetString(content, 0, 4).ShouldBe("%PDF");
                break;
        }
    }

    [Fact]
    public async Task ExportFlights_WithSearchFilter_ReturnsOnlyMatchingRows()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var matchingFlightNumber = await SeedFlightAsync();
        await SeedFlightAsync();

        var response = await admin.GetAsync(
            $"{OperationsApiFactory.Base}/flights/export?format=csv&search={Uri.EscapeDataString(matchingFlightNumber)}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var content = await response.Content.ReadAsByteArrayAsync();
        var csv = Encoding.UTF8.GetString(content, 3, content.Length - 3);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines.Length.ShouldBe(2);
        lines[1].ShouldContain(matchingFlightNumber);
    }

    private async Task<string> SeedFlightAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var flightNumber = $"RPT{Guid.NewGuid():N}"[..12];
        var flight = Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "EX", "Export Customer"),
            new StationSnapshot(Guid.NewGuid(), "DMM", "Dammam"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Report operation"),
            FlightNumber.Create(flightNumber).Value,
            ScheduledTime.Create(now, now.AddHours(2).AddMinutes(10)).Value,
            aircraftType: null,
            plannedServices: [new ServiceSnapshot(Guid.NewGuid(), "Report service")],
            assignedEmployees: [],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: now).Value;

        db.Flights.Add(flight);
        await db.SaveChangesAsync();
        return flightNumber;
    }
}
