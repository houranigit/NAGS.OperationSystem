using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Operations.Domain.Flights;
using Operations.Infrastructure.Persistence;

namespace Operations.IntegrationTests;

/// <summary>Seeds published pre-RTR schemas without inserting columns introduced by the current model.</summary>
internal static class PreRtrMigrationSeed
{
    public static async Task SaveAsync(OperationsDbContext db, params Flight[] flights)
    {
        db.Flights.AddRange(flights);
        foreach (var flight in flights)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [operations].[flights]
                    ([Id], [CustomerId], [CustomerIataCode], [CustomerName], [StationId], [StationIataCode], [StationName],
                     [OperationTypeId], [OperationTypeName], [FlightNumber], [ScheduledArrivalUtc], [ScheduledDepartureUtc],
                     [AircraftTypeId], [AircraftManufacturer], [AircraftModel], [CreatedByUserId], [CreatedAtUtc], [UpdatedAtUtc])
                VALUES ({flight.Id}, {flight.Customer.CustomerId}, {flight.Customer.IataCode}, {flight.Customer.Name},
                    {flight.Station.StationId}, {flight.Station.IataCode}, {flight.Station.Name},
                    {flight.OperationType.OperationTypeId}, {flight.OperationType.Name}, {flight.FlightNumber.Value},
                    {flight.Schedule.Sta}, {flight.Schedule.Std}, {flight.AircraftType?.AircraftTypeId},
                    {flight.AircraftType?.Manufacturer}, {flight.AircraftType?.Model}, {flight.CreatedByUserId},
                    {flight.CreatedAtUtc}, {flight.UpdatedAtUtc})
                """);
            MarkOwnerAndReferencesUnchanged(db.Entry(flight));
            var version = await db.Flights.Where(item => item.Id == flight.Id).Select(item => item.RowVersion).SingleAsync();
            db.Entry(flight).Property(item => item.RowVersion).CurrentValue = version;
            db.Entry(flight).Property(item => item.RowVersion).OriginalValue = version;

            foreach (var service in flight.Services)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [operations].[flight_services]
                        ([Id], [FlightId], [ServiceId], [ServiceName], [CustomerReferenceNumber], [FromUtc], [ToUtc],
                         [EndedByUserId], [Notes], [CreatedByUserId], [CreatedAtUtc], [UpdatedByUserId], [UpdatedAtUtc])
                    VALUES ({service.Id}, {flight.Id}, {service.Service.ServiceId}, {service.Service.Name},
                        {service.CustomerReferenceNumber}, {service.Window.FromUtc}, {service.Window.ToUtc},
                        {service.EndedByUserId}, {service.Notes}, {service.CreatedByUserId}, {service.CreatedAtUtc},
                        {service.UpdatedByUserId}, {service.UpdatedAtUtc})
                    """);
                MarkOwnerAndReferencesUnchanged(db.Entry(service));
            }

            foreach (var task in flight.Tasks)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [operations].[flight_tasks]
                        ([Id], [FlightId], [TaskType], [AtaChapterId], [AtaChapterCode], [AtaChapterTitle],
                         [CustomerReferenceNumber], [FromUtc], [ToUtc], [EndedByUserId], [Notes],
                         [CreatedByUserId], [CreatedAtUtc], [UpdatedByUserId], [UpdatedAtUtc])
                    VALUES ({task.Id}, {flight.Id}, {(int)task.TaskType}, {task.AtaChapter?.AtaChapterId},
                        {task.AtaChapter?.Code}, {task.AtaChapter?.Title},
                        {task.CustomerReferenceNumber}, {task.Window.FromUtc}, {task.Window.ToUtc}, {task.EndedByUserId}, {task.Notes},
                        {task.CreatedByUserId}, {task.CreatedAtUtc}, {task.UpdatedByUserId}, {task.UpdatedAtUtc})
                    """);
                MarkOwnerAndReferencesUnchanged(db.Entry(task));
            }
        }

        await db.SaveChangesAsync();
    }

    private static void MarkOwnerAndReferencesUnchanged(EntityEntry entry)
    {
        entry.State = EntityState.Unchanged;
        foreach (var reference in entry.References.Where(reference => reference.Metadata.TargetEntityType.IsOwned()))
            if (reference.TargetEntry is { } owned)
                MarkOwnerAndReferencesUnchanged(owned);
    }
}
