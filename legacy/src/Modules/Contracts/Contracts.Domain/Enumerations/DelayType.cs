namespace Contracts.Domain.Enumerations;

/// <summary>Which delay dimension the plan evaluates.</summary>
public enum DelayType
{
    EarlyArrival = 0,
    LateDeparture = 1,
    ActualOnGround = 2
}
