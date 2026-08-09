namespace MasterData.Contracts.Resources;

/// <summary>
/// Determines how the usage of a tool, material, or general-support catalog item is recorded.
/// Numeric values are part of the persisted and cross-module contract and must remain stable.
/// </summary>
public enum ResourceCalculationType
{
    Quantity = 0,
    Duration = 1
}
