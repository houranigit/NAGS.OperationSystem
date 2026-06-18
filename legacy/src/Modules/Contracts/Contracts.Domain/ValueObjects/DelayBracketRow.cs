namespace Contracts.Domain.ValueObjects;

/// <summary>
/// Single row of a <see cref="DelayChargePlan"/>. <see cref="MaxMinutes"/> = null marks the
/// open-ended last bracket. Waiver gaps between rows are allowed.
/// </summary>
public sealed record DelayBracketRow(int MinMinutes, int? MaxMinutes, decimal Value);
