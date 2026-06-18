using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Contracts.Contracts.Readers;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using Operations.Application.Features.Flight;
using Operations.Application.Features.Flight.Mobile;
using Operations.Contracts.IntegrationEvents;
using Operations.Domain.Aggregates.Flight;

namespace Operations.Application.Features.Flight.Commands.CreateFlight;

public sealed class CreateFlightCommandHandler(
    IFlightRepository flights,
    IContractReadService contractReader,
    IOutboxWriter outboxWriter,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<CreateFlightCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateFlightCommand request, CancellationToken cancellationToken)
    {
        var created = await FlightCreateHelpers.TryCreateScheduledFlightAsync(
            request.CustomerSnapshot,
            request.StationSnapshot,
            request.OperationTypeSnapshot,
            request.AircraftTypeId,
            request.FlightNumber,
            request.Sta,
            request.Std,
            request.AssignedEmployees,
            contractReader,
            cancellationToken);

        if (created.IsFailure)
            return Result<Guid>.Failure(created.Error!);

        var flight = created.Value!;
        flights.Add(flight);

        // Emit one outbox event per assignee so the notifications module fires the same way
        // as the explicit "invite teammate" flow. The aggregate already raised the matching
        // domain event for each assignee.
        foreach (var emp in flight.AssignedEmployees)
        {
            outboxWriter.Write(
                nameof(FlightEmployeeInvitedIntegrationEvent),
                JsonSerializer.Serialize(new FlightEmployeeInvitedIntegrationEvent(
                    flight.Id.Value,
                    InviterEmployeeId: Guid.Empty,
                    InviteeEmployeeId: emp.Employee.EmployeeId)));
        }

        // Real-time hint for connected mobile clients: each assignee's "my flights"
        // and (if AOG) every station-employee's AOG tab. Buffered until after the
        // transaction commits — see MobileSyncBroadcastBehavior.
        FlightMobileSyncBroadcasts.EnqueueUpsert(mobileSync, flight);

        return Result<Guid>.Success(flight.Id.Value);
    }
}
