using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Identity.Contracts;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Contracts;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

public sealed record PublishFlightShareCommand(Guid FlightId, Guid ShareId, PublishFlightShareRequest Input)
    : ICommand<FlightSharePostDto>;

public sealed class PublishFlightShareCommandHandler(IOperationsDbContext db, IMobileFlightScope scope,
    MasterDataResolver resolver, IMasterDataReader masterData, ICustomerLogoReader logos, TimeProvider clock,
    IUserDisplayNameReader? userNames = null) : ICommandHandler<PublishFlightShareCommand, FlightSharePostDto>
{
    public async Task<Result<FlightSharePostDto>> Handle(PublishFlightShareCommand request, CancellationToken ct)
    {
        var input = request.Input;
        var message = FlightShare.NormalizeMessage(input?.Message);
        var recipients = input?.StaffMemberIds?.Distinct().Order().ToArray() ?? [];
        if (request.FlightId == Guid.Empty || request.ShareId == Guid.Empty || input is null ||
            message?.Length > 2000 || recipients.Contains(Guid.Empty))
            return Error.Validation("A valid flight, share ID, station employees and message of at most 2,000 characters are required.", "Operations.FlightShare.Invalid");
        var resolved = await scope.ResolveAsync(ct);
        if (resolved.IsFailure) return resolved.Error;
        var actor = resolved.Value;
        var visibleFlights = MobileFlightShares.VisibleFlights(db, actor);
        var flight = await visibleFlights.Include(item => item.AssignedEmployees)
            .Include(item => item.CancellationSubmissions).FirstOrDefaultAsync(item => item.Id == request.FlightId, ct);
        if (flight is null) return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var existing = await db.FlightShares.AsNoTracking().Include(item => item.Recipients)
            .SingleOrDefaultAsync(item => item.Id == request.ShareId, ct);
        if (existing is not null)
            return await Existing(existing);
        if (!flight.AssignedEmployees.Any(item => item.Employee.StaffMemberId == actor.StaffMemberId))
            return Error.Forbidden("Only assigned employees can share this flight.", "Operations.FlightShare.AssignmentRequired");
        if (flight.CancellationSubmissions.Count > 0)
            return Error.Conflict("Canceled flights cannot be shared.", "Operations.FlightShare.Canceled");

        var author = await resolver.StaffMembersForStationAsync([actor.StaffMemberId], actor.StationId, ct);
        if (author.IsFailure) return author.Error;
        var employees = await resolver.StaffMembersForStationAsync(recipients, actor.StationId, ct);
        if (employees.IsFailure) return employees.Error;
        var created = FlightShare.Create(request.ShareId, flight.Id, actor.StationId, actor.UserId,
            author.Value.Single(), message, employees.Value, clock.GetUtcNow());
        if (created.IsFailure) return created.Error;
        // Sharing owns its own immutable rows; do not attach or touch the operational flight graph.
        db.FlightShares.Add(created.Value);
        var audience = recipients.Length == 0
            ? (await masterData.GetActiveStaffMembersForStationAsync(actor.StationId, ct))
                .Where(employee => employee.IsActive && employee.StationId == actor.StationId)
                .Select(employee => employee.Id)
            : recipients;
        foreach (var staffId in audience.Distinct().Where(id => id != actor.StaffMemberId))
        {
            db.Enqueue(new FlightShared
            {
                ShareId = created.Value.Id,
                FlightId = flight.Id,
                FlightNumber = flight.FlightNumber.Value,
                StationId = actor.StationId,
                StaffMemberId = staffId,
                SharedByUserId = actor.UserId,
                SharedByDisplayName = created.Value.AuthorFullName,
                IsStationWide = recipients.Length == 0,
                OccurredOnUtc = created.Value.CreatedAtUtc
            });
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            var saved = await db.FlightShares.AsNoTracking().Include(item => item.Recipients)
                .SingleOrDefaultAsync(item => item.Id == request.ShareId, ct);
            if (saved is null) throw;
            return await Existing(saved);
        }
        return await Map(created.Value);

        async Task<Result<FlightSharePostDto>> Existing(FlightShare saved) =>
            saved.Matches(request.FlightId, actor.StationId, actor.UserId, message, recipients)
                ? await Map(saved)
                : Error.Conflict("This share ID has already been used for a different post.", "Operations.FlightShare.IdConflict");

        async Task<Result<FlightSharePostDto>> Map(FlightShare saved)
        {
            var mapped = await MobileFlightShares.MapAsync([saved], visibleFlights, actor, masterData, logos, userNames, ct);
            if (mapped.IsFailure) return mapped.Error;
            return mapped.Value.Count == 0
                ? Error.NotFound("Flight not found.", "Operations.Flight.NotFound")
                : mapped.Value[0];
        }
    }
}

public sealed record GetMobileFlightSharesQuery(string Direction = "all", string? Search = null, int Page = 1, int PageSize = 20)
    : IQuery<MobileFlightSharePageDto>;

public sealed class GetMobileFlightSharesQueryHandler(IOperationsDbContext db, IMobileFlightScope scope,
    IMasterDataReader masterData, ICustomerLogoReader logos, IUserDisplayNameReader? userNames = null)
    : IQueryHandler<GetMobileFlightSharesQuery, MobileFlightSharePageDto>
{
    public async Task<Result<MobileFlightSharePageDto>> Handle(GetMobileFlightSharesQuery request, CancellationToken ct)
    {
        var direction = request.Direction?.Trim().ToLowerInvariant();
        if (direction is not ("all" or "received" or "sent"))
            return Error.Validation("Choose all, received or sent shares.", "Operations.FlightShare.DirectionInvalid");
        var resolved = await scope.ResolveAsync(ct);
        if (resolved.IsFailure) return resolved.Error;
        var actor = resolved.Value;
        var paging = PageRequest.From(request.Page, request.PageSize);
        var flights = MobileFlightShares.VisibleFlights(db, actor);
        var matches = FlightListQuery.ApplySearch(flights, request.Search);
        var posts = from share in db.FlightShares.AsNoTracking()
            join flight in matches on share.FlightId equals flight.Id
            where share.StationId == actor.StationId
            select share;
        posts = direction switch
        {
            "sent" => posts.Where(item => item.AuthorUserId == actor.UserId),
            "received" => posts.Where(item => item.AuthorUserId != actor.UserId &&
                (!item.Recipients.Any() || item.Recipients.Any(recipient => recipient.StaffMemberId == actor.StaffMemberId))),
            _ => posts.Where(item => item.AuthorUserId == actor.UserId || !item.Recipients.Any() ||
                item.Recipients.Any(recipient => recipient.StaffMemberId == actor.StaffMemberId))
        };
        var total = await posts.LongCountAsync(ct);
        IReadOnlyList<FlightSharePostDto> items = [];
        if (!paging.IsOutOfRange(total))
        {
            var page = await posts.OrderByDescending(item => item.CreatedAtUtc).ThenBy(item => item.Id)
                .Skip(paging.Skip).Take(paging.PageSize).Include(item => item.Recipients).ToListAsync(ct);
            var mapped = await MobileFlightShares.MapAsync(page, flights, actor, masterData, logos, userNames, ct);
            if (mapped.IsFailure) return mapped.Error;
            items = mapped.Value;
        }
        return new MobileFlightSharePageDto(items, paging.Page, paging.PageSize, total,
            actor.StationId, actor.AsOfUtc, actor.WindowStartUtc, actor.WindowEndUtc);
    }
}

internal static class MobileFlightShares
{
    internal static IQueryable<Flight> VisibleFlights(IOperationsDbContext db, MobileFlightScopeContext actor) =>
        MobileFlightQuery.ApplyReadScope(db.Flights.AsNoTracking(), actor).Where(flight =>
            flight.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType &&
            !flight.PlannedServices.Any(item => item.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService));

    internal static async Task<Result<IReadOnlyList<FlightSharePostDto>>> MapAsync(IReadOnlyList<FlightShare> posts,
        IQueryable<Flight> visibleFlights, MobileFlightScopeContext actor, IMasterDataReader masterData,
        ICustomerLogoReader logos, IUserDisplayNameReader? userNames, CancellationToken ct)
    {
        var station = await masterData.GetStationAsync(actor.StationId, ct);
        if (station is null || string.IsNullOrWhiteSpace(station.TimeZoneId))
            return Error.Failure("Flight data could not be loaded. Please try again.", "Operations.Flight.StationDataUnavailable");
        var ids = posts.Select(item => item.FlightId).Distinct().ToArray();
        var cards = await MobileFlightQuery.ProjectListItems(visibleFlights.Where(item => ids.Contains(item.Id)), actor,
            station.TimeZoneId).ToListAsync(ct);
        var versions = await logos.GetLogoVersionsAsync(cards.Select(item => item.CustomerId).Distinct().ToArray(), ct);
        var names = await FlightWorkAttribution.NamesAsync(userNames, cards.Select(item => item.CreatedByUserId), ct);
        var byId = cards.ToDictionary(item => item.Id, item => item with
        {
            CustomerLogoVersion = versions.GetValueOrDefault(item.CustomerId) ?? CustomerLogoVersions.None,
            CreatedByDisplayName = item.CreatedByUserId is { } creator ? names.GetValueOrDefault(creator) : null
        });
        return posts.Where(item => byId.ContainsKey(item.FlightId)).Select(item => new FlightSharePostDto(item.Id,
            item.FlightId, item.AuthorStaffMemberId, item.AuthorFullName, item.AuthorEmployeeId, item.Message,
            item.CreatedAtUtc, item.Recipients.Count == 0, item.Recipients.OrderBy(person => person.FullName).ThenBy(person => person.StaffMemberId)
                .Select(person => new AssignedEmployeeDto(person.StaffMemberId, person.FullName, person.EmployeeId)).ToList(),
            byId[item.FlightId], station.Name)).ToList();
    }
}
