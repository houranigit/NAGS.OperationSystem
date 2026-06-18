using Contracts.Domain.Enumerations;

namespace Contracts.Contracts.Contract;

public sealed record CancellationPlan(
    CancellationChargeBasis Basis,
    FeeType ChargeType,
    IReadOnlyList<PlanBracket> Brackets);

public sealed record DelayPlan(
    DelayType DelayType,
    DelayChargeBasis Basis,
    FeeType ChargeType,
    IReadOnlyList<PlanBracket> Brackets);

public sealed record PlanBracket(int MinMinutes, int? MaxMinutes, decimal Value, int SortOrder);
