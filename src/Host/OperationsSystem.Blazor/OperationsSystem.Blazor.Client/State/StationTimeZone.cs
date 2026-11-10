namespace OperationsSystem.Blazor.Client.State;

/// <summary>Converts operation inputs using an explicitly configured station zone.</summary>
public sealed class StationTimeZone
{
    private readonly TimeZoneInfo timeZone;

    private StationTimeZone(TimeZoneInfo timeZone) => this.timeZone = timeZone;

    public string Id => timeZone.Id;

    public static StationTimeZone FromId(string id) =>
        new(TimeZoneInfo.FindSystemTimeZoneById(id));

    public DateTime ToLocalDateTime(DateTimeOffset value) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(value, timeZone).DateTime, DateTimeKind.Unspecified);

    public string Format(DateTimeOffset value, string format = "yyyy-MM-dd HH:mm") =>
        ToLocalDateTime(value).ToString(format);

    public bool TryToUtc(DateTime? localValue, out DateTimeOffset? utcValue, out string? error) =>
        UserTimeZone.TryToUtc(localValue, timeZone, out utcValue, out error);

    public DateTimeOffset ToUtc(DateTime value)
    {
        if (!TryToUtc(value, out var utcValue, out var error))
            throw new InvalidOperationException(error);

        return utcValue!.Value;
    }

    public DateTimeOffset DateBoundaryUtc(DateTime date, bool endOfDay) =>
        UserTimeZone.DateBoundaryUtc(date, endOfDay, timeZone);
}
