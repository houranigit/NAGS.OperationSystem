using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;

namespace Operations.Application.Features.Flights;

public sealed record GetFlightCustomerLogoQuery(Guid CustomerId) : IQuery<CustomerLogoReadContent>;

public sealed class GetFlightCustomerLogoQueryHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    ICustomerLogoReader logos) : IQueryHandler<GetFlightCustomerLogoQuery, CustomerLogoReadContent>
{
    public async Task<Result<CustomerLogoReadContent>> Handle(GetFlightCustomerLogoQuery request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        var visibleFlights = FlightListQuery.ApplyScopeAndFilters(db.Flights.AsNoTracking(), resolved.Value,
            new FlightListFilter(null, null, request.CustomerId, null, null, null));
        if (!await visibleFlights.AnyAsync(cancellationToken))
            return LogoNotFound();

        // MasterData owns logo references and storage. Read only after flight visibility is established.
        var logo = await logos.GetLogoAsync(request.CustomerId, cancellationToken);
        return logo is null ? LogoNotFound() : logo;
    }

    private static Error LogoNotFound() =>
        Error.NotFound("Customer logo not found.", "Operations.Flight.CustomerLogoNotFound");
}
