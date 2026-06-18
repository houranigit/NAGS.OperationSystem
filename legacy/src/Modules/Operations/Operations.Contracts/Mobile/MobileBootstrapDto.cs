namespace Operations.Contracts.Mobile;

/// <summary>
/// One-shot payload the mobile client requests at sign-in / app-start so it can
/// hydrate every screen offline without paying N+1 round-trips. Bundles the lookup
/// blob (every catalog the work-order forms use), every flight the user is rostered
/// on inside the configured rolling window — each with its full
/// <see cref="MobileFlightContextDto"/> already attached so tapping a scheduled
/// flight never needs another fetch — and every AOG flight at the user's station
/// inside the same window so the AOG tab works fully offline too.
/// </summary>
public sealed record MobileBootstrapDto(
    MobileLookupsDto Lookups,
    IReadOnlyList<MobileBootstrapFlightDto> Flights,
    IReadOnlyList<MobileBootstrapFlightDto> AogFlights,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Pair of (summary, context) returned per flight in the bootstrap payload.
/// Mirrors what the existing N+1 calls return so the mobile coordinator can
/// upsert each flight row + context snapshot identically to today's flow.
/// </summary>
public sealed record MobileBootstrapFlightDto(
    MobileFlightSummaryDto Summary,
    MobileFlightContextDto Context);
