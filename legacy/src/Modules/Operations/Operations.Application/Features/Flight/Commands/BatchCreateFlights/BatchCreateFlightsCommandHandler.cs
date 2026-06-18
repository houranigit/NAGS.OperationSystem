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

namespace Operations.Application.Features.Flight.Commands.BatchCreateFlights;

public sealed class BatchCreateFlightsCommandHandler(
    IFlightRepository flights,
    IContractReadService contractReader,
    IOutboxWriter outboxWriter,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<BatchCreateFlightsCommand, IReadOnlyList<Guid>>
{
    public async Task<Result<IReadOnlyList<Guid>>> Handle(BatchCreateFlightsCommand request, CancellationToken cancellationToken)
    {
        if (request.Flights is null || request.Flights.Count == 0)
        {
            return Result<IReadOnlyList<Guid>>.Failure(
                Error.Validation("At least one flight is required for batch create."));
        }

        var ids = new List<Guid>(request.Flights.Count);
        for (var i = 0; i < request.Flights.Count; i++)
        {
            var item = request.Flights[i];
            var row = await FlightCreateHelpers.TryCreateScheduledFlightAsync(
                item.CustomerSnapshot,
                item.StationSnapshot,
                item.OperationTypeSnapshot,
                item.AircraftTypeId,
                item.FlightNumber,
                item.Sta,
                item.Std,
                item.AssignedEmployees,
                contractReader,
                cancellationToken);

            if (row.IsFailure)
            {
                return Result<IReadOnlyList<Guid>>.Failure(
                    Error.Validation($"Flight at index {i}: {row.Error!.Description}"));
            }

            var flight = row.Value!;
            flights.Add(flight);
            ids.Add(flight.Id.Value);

            foreach (var emp in flight.AssignedEmployees)
            {
                outboxWriter.Write(
                    nameof(FlightEmployeeInvitedIntegrationEvent),
                    JsonSerializer.Serialize(new FlightEmployeeInvitedIntegrationEvent(
                        flight.Id.Value,
                        InviterEmployeeId: Guid.Empty,
                        InviteeEmployeeId: emp.Employee.EmployeeId)));
            }

            // Per-flight mobile push. The broadcaster dedups by (table, id, op)
            // so even bulk imports keep the wire lean — one envelope per flight
            // per (employee | station) audience.
            FlightMobileSyncBroadcasts.EnqueueUpsert(mobileSync, flight);
        }

        return Result<IReadOnlyList<Guid>>.Success(ids);
    }
}
