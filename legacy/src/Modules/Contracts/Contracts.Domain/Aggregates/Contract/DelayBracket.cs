using BuildingBlocks.Domain.Entities;

namespace Contracts.Domain.Aggregates.Contract;

/// <summary>
/// Persisted row of a <see cref="ValueObjects.DelayChargePlan"/>. Stored as a child entity
/// so EF can index / query rows individually rather than serialising a JSON blob.
/// </summary>
public sealed class DelayBracket : Entity<DelayBracketId>
{
    public ContractId ContractId { get; private set; } = null!;
    public int MinMinutes { get; private set; }
    public int? MaxMinutes { get; private set; }
    public decimal Value { get; private set; }
    public int SortOrder { get; private set; }

    private DelayBracket() { }

    internal static DelayBracket Create(
        ContractId contractId,
        int minMinutes,
        int? maxMinutes,
        decimal value,
        int sortOrder) =>
        new()
        {
            Id = DelayBracketId.New(),
            ContractId = contractId,
            MinMinutes = minMinutes,
            MaxMinutes = maxMinutes,
            Value = value,
            SortOrder = sortOrder
        };
}
