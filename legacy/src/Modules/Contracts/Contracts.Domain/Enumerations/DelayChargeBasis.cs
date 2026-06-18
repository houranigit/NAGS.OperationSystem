namespace Contracts.Domain.Enumerations;

/// <summary>
/// How a delay charge is evaluated.
/// Brackets — graduated charge based on duration of the delay.
/// PerDelay — a single charge per delay event regardless of duration.
/// </summary>
public enum DelayChargeBasis
{
    Brackets = 0,
    PerDelay = 1
}
