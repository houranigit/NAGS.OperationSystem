using BuildingBlocks.Application.Abstractions.Queries;
using Operations.Contracts.Mobile;

namespace Operations.Application.Features.Mobile.Queries.GetMobileLookups;

/// <summary>
/// Mobile-only lookup bundle: aircraft types, services, tools, materials, general
/// supports, active customers, and the calling employee's station roster. Lets the
/// Android app pre-fetch every dropdown once at sign-in so the work-order forms
/// (scheduled and ad-hoc) work fully offline. <paramref name="StationId"/> scopes
/// the employee roster to the caller's station — pass <see cref="System.Guid.Empty"/>
/// to skip the roster (the endpoint resolves the station id from the caller's JWT).
/// </summary>
public sealed record GetMobileLookupsQuery(Guid StationId) : IQuery<MobileLookupsDto>;
