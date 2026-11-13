using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using Identity.Contracts;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flights;

public sealed record GetMobileAdHocFlightContextQuery : IQuery<MobileAdHocFlightContextDto>;
public sealed record CreateMobileAdHocFlightCommand(Guid FlightId, SaveMobileAdHocFlightDetailsRequest Input) : ICommand<FlightDetailDto>;
public sealed record UpdateMobileAdHocFlightDetailsCommand(Guid FlightId, SaveMobileAdHocFlightDetailsRequest Input, byte[] RowVersion) : ICommand<FlightDetailDto>;

public sealed class GetMobileAdHocFlightContextQueryHandler(IMobileFlightScope scope, MasterDataResolver resolver,
    IMasterDataReader masterData) : IQueryHandler<GetMobileAdHocFlightContextQuery, MobileAdHocFlightContextDto>
{
    public async Task<Result<MobileAdHocFlightContextDto>> Handle(GetMobileAdHocFlightContextQuery request, CancellationToken ct)
    {
        var actor = await scope.ResolveAsync(ct);
        if (actor.IsFailure) return actor.Error;
        var zone = await resolver.StationTimeZoneAsync(actor.Value.StationId, ct);
        if (zone.IsFailure) return zone.Error;
        var station = await masterData.GetStationAsync(actor.Value.StationId, ct);
        if (station is null) return Error.Failure("Station data could not be loaded.", "Operations.Flight.StationDataUnavailable");
        var allowed = await resolver.AllowedServiceIdsForStaffAsync(actor.Value.StaffMemberId, actor.Value.StationId, ct);
        if (allowed.IsFailure) return allowed.Error;
        return new MobileAdHocFlightContextDto(station.Id, station.IataCode, station.Name, station.TimeZoneId,
            actor.Value.StaffMemberId, true, true, true, allowed.Value.Count > 0, true, allowed.Value,
            actor.Value.AsOfUtc, actor.Value.WindowStartUtc, actor.Value.WindowEndUtc);
    }
}

public sealed class CreateMobileAdHocFlightCommandHandler(IOperationsDbContext db, IMobileFlightScope scope,
    MasterDataResolver resolver, IMasterDataReader masterData, TimeProvider clock, IMobileSyncBroadcaster mobileSync,
    IUserDisplayNameReader? userNames = null) : ICommandHandler<CreateMobileAdHocFlightCommand, FlightDetailDto>
{
    public async Task<Result<FlightDetailDto>> Handle(CreateMobileAdHocFlightCommand request, CancellationToken ct)
    {
        if (request.FlightId == Guid.Empty) return MobileAdHocFlights.Invalid();
        var parsed = MobileAdHocFlights.Parse(request.Input);
        if (parsed.IsFailure) return parsed.Error;
        var resolved = await scope.ResolveAsync(ct);
        if (resolved.IsFailure) return resolved.Error;
        var actor = resolved.Value;
        var fingerprint = MobileAdHocFlights.Fingerprint(actor.StationId, request.Input, parsed.Value);
        var existing = await MobileAdHocFlights.Load(db.Flights.AsNoTracking()).SingleOrDefaultAsync(item => item.Id == request.FlightId, ct);
        if (existing is not null) return await ReplayAsync(existing);
        var window = MobileAdHocFlights.InWindow(parsed.Value.Schedule, actor);
        if (window.IsFailure) return window.Error;
        var references = await MobileAdHocFlights.ReferencesAsync(resolver, request.Input.CustomerId, actor.StationId, ct);
        if (references.IsFailure) return references.Error;
        var refs = references.Value;
        var created = Flight.CreateAdHoc(refs.Customer, refs.Station, refs.OperationType, parsed.Value.Number,
            parsed.Value.Schedule, actor.UserId, clock.GetUtcNow(), request.FlightId, fingerprint);
        if (created.IsFailure) return created.Error;
        db.Flights.Add(created.Value);
        await FlightActivityRecorder.TouchAsync(db, created.Value, actor, clock.GetUtcNow(), ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // A concurrent identical creation can win the primary key before this request publishes.
            var persisted = await MobileAdHocFlights.Load(db.Flights.AsNoTracking()).SingleOrDefaultAsync(item => item.Id == request.FlightId, ct);
            if (persisted is not null) return await ReplayAsync(persisted);
            throw;
        }
        FlightServiceWrites.Notify(mobileSync, clock);
        return await MobileAdHocFlights.MapAsync(created.Value, actor, resolver, masterData, userNames, ct);

        async Task<Result<FlightDetailDto>> ReplayAsync(Flight flight)
        {
            if (flight.CreatedByUserId != actor.UserId || flight.Station.StationId != actor.StationId ||
                flight.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType || flight.AdHocCreationFingerprint != fingerprint)
                return Error.Conflict("This flight creation ID is already in use.", "Operations.AdHoc.IdConflict");
            var visible = MobileAdHocFlights.InWindow(flight.Schedule, actor);
            if (visible.IsFailure) return visible.Error;
            // The immutable receipt acknowledges creation even after current planning or work changes.
            return await MobileAdHocFlights.MapAsync(flight, actor, resolver, masterData, userNames, ct);
        }
    }
}

public sealed class UpdateMobileAdHocFlightDetailsCommandHandler(IOperationsDbContext db, IMobileFlightScope scope,
    MasterDataResolver resolver, IMasterDataReader masterData, TimeProvider clock, IMobileSyncBroadcaster mobileSync,
    IUserDisplayNameReader? userNames = null) : ICommandHandler<UpdateMobileAdHocFlightDetailsCommand, FlightDetailDto>
{
    public async Task<Result<FlightDetailDto>> Handle(UpdateMobileAdHocFlightDetailsCommand request, CancellationToken ct)
    {
        if (request.FlightId == Guid.Empty || request.RowVersion is not { Length: 8 }) return MobileAdHocFlights.Invalid();
        var parsed = MobileAdHocFlights.Parse(request.Input);
        if (parsed.IsFailure) return parsed.Error;
        var resolved = await scope.ResolveAsync(ct);
        if (resolved.IsFailure) return resolved.Error;
        var actor = resolved.Value;
        var flight = await MobileAdHocFlights.Load(MobileFlightQuery.ApplyReadScope(db.Flights, actor))
            .SingleOrDefaultAsync(item => item.Id == request.FlightId && item.OperationType.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType, ct);
        if (flight is null) return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");
        if (flight.CreatedByUserId != actor.UserId)
            return Error.Forbidden("Only its creator can change AdHoc flight details.", "Operations.AdHoc.DetailsReadOnly");
        if (flight.Customer.CustomerId == request.Input.CustomerId && flight.FlightNumber == parsed.Value.Number && flight.Schedule == parsed.Value.Schedule)
            return await MobileAdHocFlights.MapAsync(flight, actor, resolver, masterData, userNames, ct);
        if (flight.HasOperationalRecords())
            return Error.Conflict("Flight details are locked after operational work is saved.", "Operations.AdHoc.DetailsLocked");
        if (!flight.RowVersion.SequenceEqual(request.RowVersion)) return ConcurrencyErrors.Stale;
        var window = MobileAdHocFlights.InWindow(parsed.Value.Schedule, actor);
        if (window.IsFailure) return window.Error;
        var references = await MobileAdHocFlights.ReferencesAsync(resolver, request.Input.CustomerId, actor.StationId, ct);
        if (references.IsFailure) return references.Error;
        var changed = flight.UpdateAdHocDetails(references.Value.Customer, parsed.Value.Number, parsed.Value.Schedule, actor.UserId, clock.GetUtcNow());
        if (changed.IsFailure) return changed.Error;
        db.SetOriginalRowVersion(flight, request.RowVersion);
        await FlightActivityRecorder.TouchAsync(db, flight, actor, clock.GetUtcNow(), ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return ConcurrencyErrors.Stale; }
        FlightServiceWrites.Notify(mobileSync, clock);
        return await MobileAdHocFlights.MapAsync(flight, actor, resolver, masterData, userNames, ct);
    }
}

internal static class MobileAdHocFlights
{
    internal sealed record ParsedDetails(FlightNumber Number, ScheduledTime Schedule);
    internal sealed record References(CustomerSnapshot Customer, StationSnapshot Station, OperationTypeSnapshot OperationType);

    internal static Result<ParsedDetails> Parse(SaveMobileAdHocFlightDetailsRequest? input)
    {
        if (input is null || input.CustomerId == Guid.Empty || input.ScheduledArrivalUtc == default || input.ScheduledDepartureUtc == default) return Invalid();
        var number = FlightNumber.CreateAdHoc(input.FlightNumber);
        if (number.IsFailure) return number.Error;
        var schedule = ScheduledTime.Create(input.ScheduledArrivalUtc, input.ScheduledDepartureUtc);
        return schedule.IsFailure ? schedule.Error : new ParsedDetails(number.Value, schedule.Value);
    }

    internal static Result InWindow(ScheduledTime schedule, MobileFlightScopeContext actor) =>
        schedule.Sta <= actor.WindowEndUtc && schedule.Std >= actor.WindowStartUtc ? Result.Success() :
            Error.Validation("The flight schedule must overlap the current mobile window.", "Operations.Mobile.ScheduleOutsideWindow");

    internal static string Fingerprint(Guid stationId, SaveMobileAdHocFlightDetailsRequest input, ParsedDetails parsed) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object[]
            { stationId, input.CustomerId, parsed.Number.Value, parsed.Schedule.Sta, parsed.Schedule.Std }))));

    internal static async Task<Result<References>> ReferencesAsync(MasterDataResolver resolver, Guid customerId, Guid stationId, CancellationToken ct)
    {
        var customer = await resolver.CustomerAsync(customerId, ct);
        if (customer.IsFailure) return customer.Error;
        var station = await resolver.StationAsync(stationId, ct);
        if (station.IsFailure) return station.Error;
        var zone = await resolver.StationTimeZoneAsync(stationId, ct);
        if (zone.IsFailure) return zone.Error;
        var operation = await resolver.OperationTypeAsync(WellKnownMasterDataIds.AdHocOperationType, ct);
        return operation.IsFailure ? operation.Error : new References(customer.Value, station.Value, operation.Value);
    }

    internal static IQueryable<Flight> Load(IQueryable<Flight> query) => query.AsSplitQuery()
        .Include(item => item.PlannedServices).Include(item => item.AssignedEmployees)
        .Include(item => item.ArrivalSubmissions).Include(item => item.DepartureSubmissions).Include(item => item.CancellationSubmissions)
        .Include(item => item.ReturnToRamps)
        .Include(item => item.ServiceRecords).ThenInclude(item => item.Performers)
        .Include(item => item.ServiceRecords).ThenInclude(item => item.Attachments)
        .Include(item => item.TaskRecords).ThenInclude(item => item.Performers)
        .Include(item => item.TaskRecords).ThenInclude(item => item.Tools)
        .Include(item => item.TaskRecords).ThenInclude(item => item.Materials)
        .Include(item => item.TaskRecords).ThenInclude(item => item.GeneralSupports)
        .Include(item => item.TaskRecords).ThenInclude(item => item.Attachments);

    internal static async Task<Result<FlightDetailDto>> MapAsync(Flight flight, MobileFlightScopeContext actor,
        MasterDataResolver resolver, IMasterDataReader masterData, IUserDisplayNameReader? userNames, CancellationToken ct)
    {
        var station = await masterData.GetStationAsync(actor.StationId, ct);
        if (station is null || string.IsNullOrWhiteSpace(station.TimeZoneId))
            return Error.Failure("Flight data could not be loaded.", "Operations.Flight.StationDataUnavailable");
        var allowed = await resolver.AllowedServiceIdsForStaffAsync(actor.StaffMemberId, actor.StationId, ct);
        var ids = allowed.IsSuccess ? allowed.Value : [];
        var names = await FlightWorkAttribution.NamesAsync(userNames, FlightWorkAttribution.UserIds(flight), ct);
        var ownCancellation = flight.CancellationSubmissions.Any(item => item.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId);
        var ownWork = flight.ArrivalSubmissions.Any(item => item.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId) ||
            flight.DepartureSubmissions.Any(item => item.SubmittedByEmployee.StaffMemberId == actor.StaffMemberId);
        return FlightDetails.Map(flight, station.TimeZoneId, actor.StaffMemberId, !ownCancellation, !ownCancellation,
            !ownCancellation && !ownWork && flight.ServiceRecords.Count == 0 && flight.TaskRecords.Count == 0,
            flight.CancellationSubmissions.Count == 0 && ids.Count > 0, actor.UserId, ids, flight.CancellationSubmissions.Count == 0,
            flight.CanCorrectOperationalWork(actor.StaffMemberId), names, canEditFlightDetails: true);
    }

    internal static Error Invalid() => Error.Validation("Customer, flight number, scheduled arrival and departure are required.", "Operations.AdHoc.RequestInvalid");
}
