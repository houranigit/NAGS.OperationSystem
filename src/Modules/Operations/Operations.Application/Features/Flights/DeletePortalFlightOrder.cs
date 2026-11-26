using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Auditing;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Contracts.Auditing;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

public enum FlightOrderKind { Work, Cancellation }

public sealed record DeletePortalFlightOrderCommand(Guid FlightId, FlightOrderKind Kind, byte[] RowVersion) : ICommand;

public sealed class DeletePortalFlightOrderCommandHandler(IOperationsDbContext db, PortalFlightScope scope,
    IAuditContext audit, IFileStorage storage, IMobileSyncBroadcaster mobileSync, TimeProvider clock, IFlightWorkOrderEmailQueue workOrderEmails)
    : ICommandHandler<DeletePortalFlightOrderCommand>
{
    public async Task<Result> Handle(DeletePortalFlightOrderCommand request, CancellationToken cancellationToken)
    {
        if (request.RowVersion is not { Length: 8 })
            return ConcurrencyErrors.PreconditionRequired;

        var access = await scope.ResolveOrderDeletionAsync(cancellationToken);
        if (access.IsFailure) return access.Error;

        var flight = await FlightServiceWrites.Load(access.Value.Apply(db.Flights))
            .SingleOrDefaultAsync(item => item.Id == request.FlightId, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");
        if (!flight.RowVersion.SequenceEqual(request.RowVersion))
            return ConcurrencyErrors.Stale;

        var now = clock.GetUtcNow();
        var storageKeys = request.Kind == FlightOrderKind.Work ? WorkOrderStorageKeys(flight) : [];
        var deletion = request.Kind switch
        {
            FlightOrderKind.Work => flight.DeleteWorkOrder(now),
            FlightOrderKind.Cancellation => flight.DeleteCancellationOrder(now),
            _ => Result.Failure<bool>(Error.Validation("The order type is invalid.", "Operations.Flight.OrderTypeInvalid"))
        };
        if (deletion.IsFailure) return deletion.Error;
        if (!deletion.Value) return Result.Success();

        var orderType = request.Kind == FlightOrderKind.Work ? "WorkOrder" : "CancellationOrder";
        db.EnqueueAudit(audit, "operations", "Flight", flight.Id, orderType, flight.Id, AuditActions.Deleted);
        db.SetOriginalRowVersion(flight, request.RowVersion);
        var receipt = await workOrderEmails.EnqueueAsync(flight, access.Value.UserId, cancellationToken);
        if (receipt.IsFailure) return receipt.Error;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
        catch
        {
            // If the commit result is unknown, remove only files without a persisted reference.
            await FlightServiceFiles.DeleteUnreferencedAsync(db, storage, storageKeys);
            throw;
        }

        await FlightServiceFiles.DeleteUnreferencedAsync(db, storage, storageKeys);
        FlightServiceWrites.Notify(mobileSync, clock);
        return Result.Success();
    }

    private static string[] WorkOrderStorageKeys(Flight flight) =>
        flight.ServiceRecords.SelectMany(record => record.Attachments.Select(file => file.Details.StorageKey))
            .Concat(flight.TaskRecords.SelectMany(record => record.Attachments.Select(file => file.Details.StorageKey)))
            .Concat(flight.DepartureSubmissions.Select(record => record.Details.CustomerSignature?.StorageKey).OfType<string>())
            .Concat(flight.ReturnToRamps.Select(record => record.DepartureDetails?.CustomerSignature?.StorageKey).OfType<string>())
            .ToArray();
}
