namespace Contracts.Domain.ValueObjects;

/// <summary>
/// Single row of a <see cref="CancellationChargePlan"/>. <see cref="MaxMinutes"/> = null
/// marks the open-ended last bracket. Waiver gaps between rows are allowed.
/// </summary>
public sealed record CancellationBracketRow(int MinMinutes, int? MaxMinutes, decimal Value);
