using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;

namespace Operations.Application.Features.Flights;

public sealed record GetMobileFlightReturnToRampSignatureQuery(Guid FlightId, Guid OccurrenceId, DateTimeOffset ExpectedUpdatedAt, bool PortalOnly = false)
    : IQuery<FlightDepartureSignatureContent>;
public sealed class GetMobileFlightReturnToRampSignatureQueryHandler(IOperationsDbContext db, IMobileFlightScope scope, IFileStorage storage,
    PortalFlightScope? portalScope = null)
    : IQueryHandler<GetMobileFlightReturnToRampSignatureQuery, FlightDepartureSignatureContent>
{
    public async Task<Result<FlightDepartureSignatureContent>> Handle(GetMobileFlightReturnToRampSignatureQuery request, CancellationToken ct)
    {
        var resolved = await FlightOperationAccess.ResolveReadAsync(scope, portalScope, request.PortalOnly, ct);
        if (resolved.IsFailure) return resolved.Error;
        var flight = await resolved.Value.Apply(db.Flights.AsNoTracking()).Include(f => f.ReturnToRamps)
            .SingleOrDefaultAsync(f => f.Id == request.FlightId, ct);
        var occurrence = flight?.ReturnToRamps.SingleOrDefault(r => r.Id == request.OccurrenceId);
        if (occurrence?.DepartureDetails?.CustomerSignature is not { } signature) return Missing();
        if ((occurrence.UpdatedAtUtc ?? occurrence.CreatedAtUtc) != request.ExpectedUpdatedAt)
            return Error.PreconditionFailed("The RTR signature changed. Reload and retry.", "Operations.ReturnToRamp.SignatureChanged");
        var content = await storage.OpenAsync(signature.StorageKey, ct);
        return content is null ? Missing() : new FlightDepartureSignatureContent(content);
    }
    private static Error Missing() => Error.NotFound("RTR signature not found.", "Operations.ReturnToRamp.SignatureNotFound");
}
