namespace Contracts.Domain.Enumerations;

/// <summary>
/// How a cancellation charge is evaluated.
/// Brackets — graduated charge based on how far before operation start the cancellation was received.
/// PerCancel — a single charge applied regardless of timing.
/// </summary>
public enum CancellationChargeBasis
{
    Brackets = 0,
    PerCancel = 1
}
