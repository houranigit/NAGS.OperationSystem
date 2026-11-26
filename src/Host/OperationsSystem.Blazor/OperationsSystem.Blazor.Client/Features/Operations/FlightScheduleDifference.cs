namespace OperationsSystem.Blazor.Client.Features.Operations;

internal readonly record struct FlightScheduleDifference(string Text, string Description, bool IsLate)
{
    public static FlightScheduleDifference? From(TimeSpan difference)
    {
        var minutes = (long)Math.Round(difference.TotalMinutes, MidpointRounding.AwayFromZero);
        if (minutes == 0)
            return null;

        var duration = FlightStatusTime.Elapsed(TimeSpan.FromMinutes(Math.Abs(minutes)));
        var late = minutes > 0;
        return new FlightScheduleDifference(
            $"{(late ? "+" : "−")}{duration}",
            $"{duration} {(late ? "late" : "early")}",
            late);
    }
}
