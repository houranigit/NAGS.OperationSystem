using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;

namespace Operations.Application.Features.Flights;

public sealed record GetMobileFlightDepartureSignatureQuery(Guid FlightId, Guid SubmissionId, DateTimeOffset ExpectedUpdatedAt)
    : IQuery<FlightDepartureSignatureContent>;

public sealed record FlightDepartureSignatureContent(Stream Content);

public sealed class GetMobileFlightDepartureSignatureQueryHandler(
    IOperationsDbContext db,
    IMobileFlightScope scope,
    IFileStorage storage) : IQueryHandler<GetMobileFlightDepartureSignatureQuery, FlightDepartureSignatureContent>
{
    public async Task<Result<FlightDepartureSignatureContent>> Handle(
        GetMobileFlightDepartureSignatureQuery request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure) return resolved.Error;
        var flight = await MobileFlightQuery.ApplyScope(db.Flights.AsNoTracking(), resolved.Value)
            .Include(f => f.DepartureSubmissions)
            .SingleOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null) return NotFound();
        var submissions = flight.DepartureSubmissions.OrderBy(d => d.CreatedAtUtc).ThenBy(d => d.Id).ToArray();
        var visible = submissions.FirstOrDefault(d => d.SubmittedByEmployee.StaffMemberId == resolved.Value.StaffMemberId)
            ?? submissions.FirstOrDefault(d => d.IsAccepted) ?? submissions.FirstOrDefault();
        if (visible is null || visible.Id != request.SubmissionId) return NotFound();
        if ((visible.UpdatedAtUtc ?? visible.CreatedAtUtc) != request.ExpectedUpdatedAt)
            return Error.PreconditionFailed("The departure signature changed. Reload the flight and retry.",
                "Operations.Flight.DepartureSignatureChanged");
        // Storage references remain server-owned and are never sent to the client.
        var content = await storage.OpenAsync(visible.Details.CustomerSignature.StorageKey, cancellationToken);
        return content is null ? NotFound() : new FlightDepartureSignatureContent(content);
    }

    private static Error NotFound() => Error.NotFound("Departure signature not found.", "Operations.Flight.DepartureSignatureNotFound");
}
