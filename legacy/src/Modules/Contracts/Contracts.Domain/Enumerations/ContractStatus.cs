namespace Contracts.Domain.Enumerations;

/// <summary>
/// Persisted lifecycle state of a contract. "ExpiringSoon" is a computed predicate, not a state.
/// </summary>
public enum ContractStatus
{
    Draft = 0,
    Active = 1,
    Suspended = 2,
    Terminated = 3,
    Expired = 4
}
