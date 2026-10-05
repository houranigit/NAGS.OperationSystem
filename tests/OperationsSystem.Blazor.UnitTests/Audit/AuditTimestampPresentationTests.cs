using System.Reflection;
using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Features.Audit.Components;
using OperationsSystem.Blazor.Client.Localization;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Audit;

public sealed class AuditTimestampPresentationTests
{
    [Theory]
    [InlineData("2026-01-05T01:30:00Z", 2026, 1, 4, 19, 30)]
    [InlineData("2026-07-05T01:30:00Z", 2026, 7, 4, 20, 30)]
    public async Task Timestamp_changes_show_the_browser_calendar_date_and_dst_offset(
        string utc, int year, int month, int day, int hour, int minute)
    {
        var dialog = await CreateAsync();

        FormatValue(dialog, "ScheduledArrivalUtc", utc)
            .ShouldBe(new DateTime(year, month, day, hour, minute, 0).ToString("g"));
    }

    [Fact]
    public async Task Date_only_changes_remain_calendar_dates()
    {
        var dialog = await CreateAsync();

        FormatValue(dialog, "CalibrationDate", "2026-10-05").ShouldBe("2026-10-05");
        FormatValue(dialog, "StartDate", "2026-01-01").ShouldBe("2026-01-01");
    }

    [Fact]
    public async Task Metadata_localizes_nested_instants_with_explicit_offsets_and_preserves_other_values()
    {
        var dialog = await CreateAsync();
        const string metadata = """
            {"scheduledArrivalUtc":"2026-10-05T01:30:00Z","date":"2026-10-05",
             "details":[{"signedAtUtc":"2026-01-05T01:30:00Z"},4,true,null],"note":"Flight updated",
             "dateNote":"Thursday, October 1, 2026"}
            """;

        using var json = JsonDocument.Parse(FormatMetadata(dialog, metadata));
        json.RootElement.GetProperty("scheduledArrivalUtc").GetString()
            .ShouldBe("2026-10-04T20:30:00.0000000-05:00");
        json.RootElement.GetProperty("details")[0].GetProperty("signedAtUtc").GetString()
            .ShouldBe("2026-01-04T19:30:00.0000000-06:00");
        json.RootElement.GetProperty("date").GetString().ShouldBe("2026-10-05");
        json.RootElement.GetProperty("details")[1].GetInt32().ShouldBe(4);
        json.RootElement.GetProperty("details")[2].GetBoolean().ShouldBeTrue();
        json.RootElement.GetProperty("details")[3].ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("note").GetString().ShouldBe("Flight updated");
        json.RootElement.GetProperty("dateNote").GetString().ShouldBe("Thursday, October 1, 2026");
    }

    [Fact]
    public async Task Indefinite_lockout_and_invalid_metadata_keep_their_existing_meaning()
    {
        var dialog = await CreateAsync();

        FormatValue(dialog, "LockoutEndUtc", "9999-12-31T23:59:59Z")
            .ShouldBe(UiStrings.Users.LockedIndefinitely);
        FormatMetadata(dialog, "Not JSON: QA timestamp diagnostic").ShouldBe("Not JSON: QA timestamp diagnostic");
    }

    private static async Task<AuditTrailDetailDialog> CreateAsync()
    {
        var zone = new UserTimeZone(new ChicagoRuntime());
        await zone.InitializeAsync();
        var dialog = new AuditTrailDetailDialog();
        typeof(AuditTrailDetailDialog).GetProperty("UserTimeZone", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(dialog, zone);
        return dialog;
    }

    private static string FormatValue(AuditTrailDetailDialog dialog, string field, string value) =>
        (string)typeof(AuditTrailDetailDialog).GetMethod("FormatValue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, [field, value])!;

    private static string FormatMetadata(AuditTrailDetailDialog dialog, string metadata) =>
        (string)typeof(AuditTrailDetailDialog).GetMethod("FormatMetadata", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, [metadata])!;

    private sealed class ChicagoRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.ShouldBe("operationsSystem.timeZone.get");
            return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>(
                """{"Id":"America/Chicago","OffsetMinutes":-300}""")!);
        }
    }
}
