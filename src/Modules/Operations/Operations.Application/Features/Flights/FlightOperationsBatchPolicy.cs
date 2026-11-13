namespace Operations.Application.Features.Flights;

/// <summary>Transport bounds for one atomic edit; individual attachment policies still apply.</summary>
public static class FlightOperationsBatchPolicy
{
    public const long MaxInlineBytes = 100L * 1024 * 1024;
    public const long MaxJsonRequestBytes = 150L * 1024 * 1024;
}
