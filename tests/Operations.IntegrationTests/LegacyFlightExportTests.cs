using ClosedXML.Excel;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Resources;
using Operations.Api.Exports;
using Operations.Application.Contracts;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class LegacyFlightExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly FlightExportCriteria Criteria = new(null, [], [], null, null, null, null, null, [], false, null);
    private static readonly Guid CustomerId = Guid.NewGuid(), StationId = Guid.NewGuid(), OperationId = Guid.NewGuid(),
        AircraftId = Guid.NewGuid(), StaffId = Guid.NewGuid(), ManpowerId = Guid.NewGuid(), ServiceId = Guid.NewGuid(),
        OtherServiceId = Guid.NewGuid(), ToolId = Guid.NewGuid(), OtherToolId = Guid.NewGuid(), MaterialId = Guid.NewGuid(),
        SupportId = Guid.NewGuid(), OtherSupportId = Guid.NewGuid(), ThirdSupportId = Guid.NewGuid();

    [Fact]
    public void LegacySheet_MirrorsEveryItemAndResolvesAllTenCatalogTypesById()
    {
        var file = Export([CreateRow()], CreateLookup());
        var samplePath = Environment.GetEnvironmentVariable("FLIGHT_EXPORT_LEGACY_SAMPLE_PATH");
        if (!string.IsNullOrWhiteSpace(samplePath))
            File.WriteAllBytes(samplePath, file.Content);
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        workbook.Worksheets.Select(sheet => sheet.Name).ShouldBe(
            ["Flights", "Service Details", "Task Details", "Flight Breakdown", "Services and Resources", "Legacy Services and Resources"]);
        var original = workbook.Worksheet("Services and Resources");
        var legacy = workbook.Worksheet("Legacy Services and Resources");
        legacy.LastRowUsed()!.RowNumber().ShouldBe(13);
        for (var row = 6; row <= 13; row++)
        {
            foreach (var column in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 15, 17, 19, 22, 24, 25, 26 })
                legacy.Cell(row, column).Value.ShouldBe(original.Cell(row, column).Value);
            legacy.Cell(row, 14).GetString().ShouldBe("CUS-100");
            legacy.Cell(row, 16).GetString().ShouldBe("STA-100");
            legacy.Cell(row, 18).GetString().ShouldBe("AC-100");
            legacy.Cell(row, 20).GetString().ShouldBe("00123");
            legacy.Cell(row, 23).GetString().ShouldBe("STAFF-100");
            legacy.Cell(row, 27).GetString().ShouldBe("OP-100");
            legacy.Cell(row, 28).GetString().ShouldBe("MAN-100");
            legacy.Cell(row, 29).IsEmpty().ShouldBeTrue();
        }
        legacy.Range(6, 21, 13, 21).Cells().Select(cell => cell.GetString()).ShouldBe(
            ["00123", "=XYZ123", "TOOL-100", "TOOL-200", "MAT-100", "SUP-100", "SUP-200", "SUP-300"]);
        original.Cell(8, 21).GetString().ShouldBe(original.Cell(9, 21).GetString());
        legacy.Cell(6, 21).DataType.ShouldBe(XLDataType.Text);
        legacy.Cell(7, 21).HasFormula.ShouldBeFalse();
        legacy.Cell(7, 21).Style.IncludeQuotePrefix.ShouldBeTrue();
        legacy.AutoFilter.IsEnabled.ShouldBeTrue();
        legacy.SheetView.SplitRow.ShouldBe(5);
        legacy.Cell(6, 5).DataType.ShouldBe(XLDataType.DateTime);
    }

    [Fact]
    public void LegacySheet_MissingMappingsRemainBlankAndAreIdentifiedWithoutNameOrGuidFallback()
    {
        var lookup = CreateLookup() with { Services = new Dictionary<Guid, string?>(), Tools = new Dictionary<Guid, string?>() };
        using var workbook = new XLWorkbook(new MemoryStream(Export([CreateRow()], lookup).Content));
        var sheet = workbook.Worksheet("Legacy Services and Resources");
        sheet.Cell(6, 20).IsEmpty().ShouldBeTrue();
        sheet.Cell(6, 21).IsEmpty().ShouldBeTrue();
        sheet.Cell(6, 29).GetString().ShouldBe("Planned service, Service");
        sheet.Cell(8, 21).IsEmpty().ShouldBeTrue();
        sheet.Cell(8, 29).GetString().ShouldBe("Planned service, Tool");
        workbook.Worksheet("Services and Resources").Cell(6, 21).GetString().ShouldBe("A Check");
    }

    [Fact]
    public void LookupRequest_UsesOnlyReferencedIdsAndIncludesIndependentRepeatedResources()
    {
        var request = FlightExportDocumentFactory.CollectLegacySystemIdRequest([CreateRow(), CreateRow()]);
        request.Services.ShouldBe([ServiceId, OtherServiceId]);
        request.Tools.ShouldBe([ToolId, OtherToolId]);
        request.Materials.ShouldBe([MaterialId]);
        request.GeneralSupports.ShouldBe([SupportId, OtherSupportId, ThirdSupportId]);
        request.Customers.ShouldBe([CustomerId]);
        request.Stations.ShouldBe([StationId]);
        request.OperationTypes.ShouldBe([OperationId]);
        request.AircraftTypes.ShouldBe([AircraftId]);
        request.StaffMembers.ShouldBe([StaffId]);
    }

    [Fact]
    public void LegacySheet_LongIdentifiersRemainExactAndHaveEnoughDisplaySpace()
    {
        var value = new string('X', 200);
        var lookup = CreateLookup() with { Services = new Dictionary<Guid, string?> { [ServiceId] = value, [OtherServiceId] = "OTHER" } };
        using var workbook = new XLWorkbook(new MemoryStream(Export([CreateRow()], lookup).Content));
        var sheet = workbook.Worksheet("Legacy Services and Resources");
        sheet.Cell(6, 21).GetString().ShouldBe(value);
        sheet.Cell(6, 21).Style.Alignment.WrapText.ShouldBeTrue();
        sheet.Row(6).Height.ShouldBeGreaterThan(26);
    }

    [Fact]
    public void LegacySheet_EmptyExportRetainsHeadersAndExistingCsvAndDashboardOutputs()
    {
        using var empty = new XLWorkbook(new MemoryStream(Export([], CreateLookup()).Content));
        var sheet = empty.Worksheet("Legacy Services and Resources");
        sheet.LastRowUsed()!.RowNumber().ShouldBe(5);
        sheet.AutoFilter.IsEnabled.ShouldBeTrue();
        sheet.Cell(5, 21).GetString().ShouldBe("Item Legacy ID");
        var row = CreateRow();
        var csv = FlightExportDocumentFactory.Create(FlightExportFormat.Csv, [row], Criteria, Now);
        var withLookup = FlightExportDocumentFactory.Create(FlightExportFormat.Csv, [row], Criteria, Now, legacySystemIds: CreateLookup());
        csv.Content.ShouldBe(withLookup.Content);
        using var dashboard = new XLWorkbook(new MemoryStream(
            FlightExportDocumentFactory.Create(FlightExportFormat.Xlsx, [row], Criteria, Now).Content));
        dashboard.Worksheets.Count.ShouldBe(5);
    }

    private static FlightExportFile Export(IReadOnlyList<FlightExportRowDto> rows, LegacySystemIdLookup lookup) =>
        FlightExportDocumentFactory.Create(FlightExportFormat.Xlsx, rows, Criteria, Now, legacySystemIds: lookup);

    private static LegacySystemIdLookup CreateLookup() => new(
        new Dictionary<Guid, string?> { [ServiceId] = "00123", [OtherServiceId] = "=XYZ123" },
        new Dictionary<Guid, string?> { [ToolId] = "TOOL-100", [OtherToolId] = "TOOL-200" },
        new Dictionary<Guid, string?> { [MaterialId] = "MAT-100" },
        new Dictionary<Guid, string?> { [SupportId] = "SUP-100", [OtherSupportId] = "SUP-200", [ThirdSupportId] = "SUP-300" },
        new Dictionary<Guid, string?> { [CustomerId] = "CUS-100" },
        new Dictionary<Guid, string?> { [StationId] = "STA-100" },
        new Dictionary<Guid, string?> { [ManpowerId] = "MAN-100" },
        new Dictionary<Guid, string?> { [OperationId] = "OP-100" },
        new Dictionary<Guid, string?> { [AircraftId] = "AC-100" },
        new Dictionary<Guid, string?> { [StaffId] = "STAFF-100" },
        new Dictionary<Guid, Guid> { [StaffId] = ManpowerId });

    private static FlightExportResourceUsageDto Resource(Guid id, string name) =>
        new(name, ResourceCalculationType.Quantity, 1, null, null) { ResourceId = id };

    private static FlightExportRowDto CreateRow() => new(
        Guid.NewGuid(), "100", "100", "SV", "Customer", "MED", "Station", "Adhoc", Now, Now.AddHours(2),
        "InProgress", false, ["A Check"], ["Engineer"],
        new ApprovedWorkOrderExportDto(null, "100", Now, Now.AddHours(2), "Boeing", "737", "HZ-123",
            ["A Check", "A Check"], ["Same tool"], ["Material"], ["Support"], "Remarks")
        {
            WorkOrderId = Guid.NewGuid(), AircraftTypeId = AircraftId, WorkOrderStatus = "Submitted",
            ServiceDetails =
            [
                new(Guid.NewGuid(), "A Check", Now, Now.AddHours(1), [], null, null) { ServiceId = ServiceId },
                new(Guid.NewGuid(), "A Check", Now, Now.AddHours(1), [], null,
                    new(Guid.NewGuid(), 1, Now, Now.AddHours(1), "Return to ramp")) { ServiceId = OtherServiceId }
            ],
            TaskDetails =
            [
                new(Guid.NewGuid(), "Major", "Task", Now, Now.AddHours(1), [],
                    [Resource(ToolId, "Same tool"), Resource(OtherToolId, "Same tool")],
                    [Resource(MaterialId, "Material")],
                    [Resource(SupportId, "Support"), Resource(OtherSupportId, "Support"), Resource(ThirdSupportId, "Support")], null)
            ]
        })
    {
        CustomerId = CustomerId, StationId = StationId, OperationTypeId = OperationId,
        PlannedServiceIds = [ServiceId], AssignedStaffMemberIds = [StaffId]
    };
}
