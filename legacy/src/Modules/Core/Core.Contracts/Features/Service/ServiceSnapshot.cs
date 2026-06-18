namespace Core.Contracts.Features.Service;

/// <summary>
/// Cross-module service snapshot. <see cref="IsAog"/> lets consumers (e.g. flight
/// creation) decide AOG-only assignment-optional behaviour without re-checking against
/// <c>CoreSeedIds.AogService</c>.
/// </summary>
public sealed record ServiceSnapshot(
    Guid ServiceId,
    string Name,
    bool IsAog = false);
