using BuildingBlocks.Domain.Entities;
using Contracts.Domain.ValueObjects;

namespace Contracts.Domain.Aggregates.Contract;

/// <summary>
/// Child entity wrapping a frozen <see cref="StationSnapshot"/> that the contract covers.
/// Owned by <see cref="Contract"/> — only mutated through aggregate methods.
/// </summary>
public sealed class ContractStation : Entity<ContractStationId>
{
    public ContractId ContractId { get; private set; } = null!;
    public StationSnapshot Station { get; private set; } = null!;

    private ContractStation() { }

    internal static ContractStation Create(ContractId contractId, StationSnapshot station) =>
        new()
        {
            Id = ContractStationId.New(),
            ContractId = contractId,
            Station = station
        };
}
