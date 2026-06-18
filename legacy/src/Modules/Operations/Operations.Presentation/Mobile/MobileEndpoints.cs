using BuildingBlocks.Domain.Results;
using Core.Contracts.Readers;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Operations.Application.Features.Flight.Commands.CancelFlight;
using Operations.Application.Features.Flight.Commands.ClaimAogFlight;
using Operations.Application.Features.Flight.Commands.CreateFromScratch;
using Operations.Application.Features.Flight.Commands.InviteEmployeeToFlight;
using Operations.Application.Features.Flight.Queries.GetMobileFlightContext;
using Operations.Application.Features.Flight.Queries.GetMyAssignedFlightsForMobile;
using Operations.Application.Features.Flight.Queries.GetMyStationAogFlights;
using Operations.Application.Features.Mobile.Queries.GetMobileBootstrap;
using Operations.Application.Features.Mobile.Queries.GetMobileLookups;
using Operations.Application.Features.WorkOrder.Commands.CreateWorkOrderForFlight;
using Operations.Application.Features.WorkOrder.Commands.RecordReturnToRampLines;
using Operations.Application.Features.WorkOrder.Commands.UpdateWorkOrder;
using Operations.Domain.Enumerations;

namespace Operations.Presentation.Mobile;

/// <summary>
/// Legacy minimal-API surface for the Android client (v1). Route registration is currently
/// disabled. New routes will be added under <c>/api/mobile/v2</c> in <see cref="MobileV2Endpoints"/>.
/// When re-enabled, v1 endpoints sit under <c>/api/mobile</c> with the <c>MobileJwt</c> policy
/// (configured in <c>Host.Web</c>) so they only accept Bearer tokens, not the portal cookie.
/// Each endpoint resolves the calling employee through <see cref="IMobileEmployeeContext"/>
/// and forces server-side flags (e.g. <c>ReturnToRamp</c>, <c>CreatedByEmployeeId</c>)
/// regardless of payload values, so the mobile UI cannot bypass the rules.
/// </summary>
public static class MobileEndpoints
{
    public static IEndpointRouteBuilder MapMobileEndpoints(this IEndpointRouteBuilder app)
    {
        // v1 `/api/mobile/*` — all route registrations commented out while rebuilding under `/api/mobile/v2`.
        // Uncomment and restore `var group = app.MapGroup("/api/mobile")…` when needed.
        //
        // var group = app.MapGroup("/api/mobile").WithTags("Mobile").RequireAuthorization("MobileJwt");
        // group.MapGet("/me", GetMeAsync);
        // group.MapGet("/lookups", GetLookupsAsync);
        // group.MapGet("/bootstrap", GetBootstrapAsync);
        // group.MapGet("/flights/my", GetMyFlightsAsync);
        // group.MapGet("/flights/aog", GetAogFlightsAsync);
        // group.MapPost("/flights/{flightId:guid}/claim", ClaimFlightAsync);
        // group.MapGet("/flights/{flightId:guid}/context", GetFlightContextAsync);
        // group.MapPost("/flights/{flightId:guid}/work-orders", CreateWorkOrderAsync);
        // group.MapPost("/flights/{flightId:guid}/cancel", CancelFlightAsync);
        // group.MapPost("/flights/{flightId:guid}/invite", InviteTeammateAsync);
        // group.MapGet("/flights/{flightId:guid}/teammates", SearchTeammatesAsync);
        // group.MapGet("/employees/at-my-station", GetMyStationEmployeesAsync);
        // group.MapPost("/work-orders/scratch", CreateWorkOrderFromScratchAsync);
        // group.MapPut("/work-orders/{workOrderId:guid}", UpdateWorkOrderAsync);
        // group.MapPost("/work-orders/{workOrderId:guid}/return-to-ramp", RecordReturnToRampAsync);
        return app;
    }

    private static async Task<IResult> GetMeAsync(
        IMobileEmployeeContext employeeContext,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        return Results.Ok(new MobileMeDto(
            me.EmployeeId,
            me.FullName,
            me.StationSnapshot.StationId,
            me.StationSnapshot.IataCode,
            me.StationSnapshot.Name,
            me.ManpowerTypeSnapshot.ManpowerTypeId,
            me.ManpowerTypeSnapshot.Name));
    }

    private static async Task<IResult> GetMyFlightsAsync(
        IMobileEmployeeContext employeeContext,
        ISender sender,
        int? page,
        int? pageSize,
        string? search,
        int? windowHours,
        int? status,
        bool? includeAog,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        FlightStatus? statusFilter = status is { } s && Enum.IsDefined(typeof(FlightStatus), s)
            ? (FlightStatus)s
            : null;

        // Default window is the new "Scheduled" tab semantic — 12-hour forward look-ahead;
        // the handler additionally filters out Completed/Canceled regardless of the
        // explicit status filter. AOG flights are excluded by default — they live on the
        // separate AOG tab where any station employee may serve them.
        var query = new GetMyAssignedFlightsForMobileQuery(
            me.EmployeeId,
            Page: page ?? 1,
            PageSize: pageSize ?? 20,
            Search: search,
            WindowHours: windowHours ?? 12,
            Status: statusFilter,
            IncludeAog: includeAog ?? false);
        var result = await sender.Send(query, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAogFlightsAsync(
        IMobileEmployeeContext employeeContext,
        ISender sender,
        int? windowHours,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var query = new GetMyStationAogFlightsQuery(me.EmployeeId, windowHours ?? 12);
        var result = await sender.Send(query, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ClaimFlightAsync(
        Guid flightId,
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var result = await sender.Send(new ClaimAogFlightCommand(flightId, me.EmployeeId), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetBootstrapAsync(
        IMobileEmployeeContext employeeContext,
        ISender sender,
        int? windowHours,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var query = new GetMobileBootstrapQuery(
            me.EmployeeId,
            me.StationSnapshot.StationId,
            windowHours ?? 12);
        var result = await sender.Send(query, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetLookupsAsync(
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        // Lookups carry the caller's station roster so the work-order pickers are
        // station-scoped without an extra round-trip.
        var result = await sender.Send(new GetMobileLookupsQuery(me.StationSnapshot.StationId), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetFlightContextAsync(
        Guid flightId,
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var result = await sender.Send(new GetMobileFlightContextQuery(flightId, me.EmployeeId), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateWorkOrderAsync(
        Guid flightId,
        MobileCreateWorkOrderRequest request,
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var services = MapServiceLines(request.ServiceLines, returnToRamp: false);
        var tasks = MapTasks(request.Tasks, returnToRamp: false);

        var command = new CreateWorkOrderForFlightCommand(
            flightId,
            request.FlightNumber,
            request.AircraftTypeId,
            request.AircraftTailNumber,
            IsCanceled: false,
            CancellationAt: null,
            Ata: request.Ata,
            Atd: request.Atd,
            ServiceLines: services,
            Tasks: tasks,
            Remarks: request.Remarks,
            CreatedByEmployeeId: me.EmployeeId,
            CustomerSignature: TryDecodeSignature(request.CustomerSignaturePng),
            ClientMutationId: request.ClientMutationId);
        var result = await sender.Send(command, ct);
        return result.ToHttpResult(payload =>
            Results.Created(
                $"/api/mobile/work-orders/{payload.WorkOrderId}",
                new MobileCreateWorkOrderResponse(payload.WorkOrderId, payload.FlightId, payload.Idempotent)));
    }

    private static async Task<IResult> CreateWorkOrderFromScratchAsync(
        MobileCreateFromScratchRequest request,
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var services = MapServiceLines(request.ServiceLines, returnToRamp: false);
        var tasks = MapTasks(request.Tasks, returnToRamp: false);

        // CustomerId comes from the client (the user picks a real airline in the
        // mobile UI). Station and operation type are forced server-side: station =
        // caller's station, OT = AdHoc seed. We never trust the client for these.
        var command = new CreateAdHocWorkOrderFromScratchCommand(
            CreatorEmployeeId: me.EmployeeId,
            CustomerId: request.CustomerId,
            FlightNumber: request.FlightNumber,
            AircraftTypeId: request.AircraftTypeId,
            AircraftTailNumber: request.AircraftTailNumber,
            Sta: request.Sta,
            Std: request.Std,
            IsCanceled: request.IsCanceled,
            CancellationAt: request.CancellationAt,
            Ata: request.Ata,
            Atd: request.Atd,
            ServiceLines: services,
            Tasks: tasks,
            Remarks: request.Remarks,
            CustomerSignature: TryDecodeSignature(request.CustomerSignaturePng),
            ClientMutationId: request.ClientMutationId,
            ClientFlightId: request.ClientFlightId);
        var result = await sender.Send(command, ct);
        return result.ToHttpResult(payload =>
            Results.Created(
                $"/api/mobile/work-orders/{payload.WorkOrderId}",
                new MobileCreateWorkOrderResponse(payload.WorkOrderId, payload.FlightId, payload.Idempotent)));
    }

    private static async Task<IResult> CancelFlightAsync(
        Guid flightId,
        MobileCancelFlightRequest? request,
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var canceledAt = request?.CanceledAt ?? DateTimeOffset.UtcNow;
        var command = new CancelFlightCommand(flightId, canceledAt, me.EmployeeId);
        var result = await sender.Send(command, ct);
        return result.ToHttpResult(workOrderId => Results.Created($"/api/mobile/work-orders/{workOrderId}", new { id = workOrderId }));
    }

    private static async Task<IResult> UpdateWorkOrderAsync(
        Guid workOrderId,
        MobileUpdateWorkOrderRequest request,
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var services = MapServiceLines(request.ServiceLines, returnToRamp: false);
        var tasks = MapTasks(request.Tasks, returnToRamp: false);

        var command = new UpdateWorkOrderCommand(
            workOrderId,
            request.FlightNumber,
            request.AircraftTypeId,
            request.AircraftTailNumber,
            IsCanceled: false,
            CancellationAt: null,
            Ata: request.Ata,
            Atd: request.Atd,
            ServiceLines: services,
            Tasks: tasks,
            Remarks: request.Remarks,
            CustomerSignature: TryDecodeSignature(request.CustomerSignaturePng),
            ClientMutationId: request.ClientMutationId);
        var result = await sender.Send(command, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RecordReturnToRampAsync(
        Guid workOrderId,
        MobileReturnToRampRequest request,
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var services = MapServiceLines(request.ServiceLines, returnToRamp: true);
        var tasks = MapTasks(request.Tasks, returnToRamp: true);

        var command = new RecordReturnToRampLinesCommand(
            workOrderId,
            services,
            tasks,
            CustomerSignature: TryDecodeSignature(request.CustomerSignaturePng),
            ClientMutationId: request.ClientMutationId);
        var result = await sender.Send(command, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> InviteTeammateAsync(
        Guid flightId,
        MobileInviteRequest request,
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var command = new InviteEmployeeToFlightCommand(flightId, request.InviteeEmployeeId, me.EmployeeId);
        var result = await sender.Send(command, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SearchTeammatesAsync(
        Guid flightId,
        IMobileEmployeeContext employeeContext,
        IEmployeeReader employeeReader,
        string? search,
        int? take,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var matches = await employeeReader.SearchActiveSnapshotsByStationAsync(
            me.StationSnapshot.StationId,
            search,
            take ?? 20,
            ct);
        return Results.Ok(matches);
    }

    /// <summary>
    /// Returns every active employee at the calling employee's station — used by the
    /// "Create work order from scratch" mobile flow to populate the service-line / task
    /// employee chips, since the flight isn't yet in Room and has no roster of its own.
    /// </summary>
    private static async Task<IResult> GetMyStationEmployeesAsync(
        IMobileEmployeeContext employeeContext,
        IEmployeeReader employeeReader,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        // 200 covers the largest stations comfortably; the mobile UI shows them as chips
        // and the user picks via tap, so we don't paginate.
        var matches = await employeeReader.SearchActiveSnapshotsByStationAsync(
            me.StationSnapshot.StationId,
            search: null,
            take: 200,
            ct);
        return Results.Ok(matches);
    }

    private static IResult NoEmployeeLinkedProfile() =>
        Results.Json(
            new
            {
                title = "Employee profile required",
                detail =
                    "This account is not linked to an employee record. In the portal: Settings → System → Employees "
                        + "→ open the employee → set Linked user to this login account.",
                code = "Mobile.EmployeeNotLinked",
                status = StatusCodes.Status403Forbidden
            },
            statusCode: StatusCodes.Status403Forbidden);

    private static List<CreateWorkOrderServiceLineInput> MapServiceLines(
        IReadOnlyList<MobileWorkOrderServiceLineInput>? source,
        bool returnToRamp) =>
        (source ?? Array.Empty<MobileWorkOrderServiceLineInput>())
            .Select(s => new CreateWorkOrderServiceLineInput(s.ServiceId, s.EmployeeId, s.From, s.To, s.Description, returnToRamp))
            .ToList();

    private static List<CreateWorkOrderTaskInput> MapTasks(
        IReadOnlyList<MobileWorkOrderTaskInput>? source,
        bool returnToRamp) =>
        (source ?? Array.Empty<MobileWorkOrderTaskInput>())
            .Select(t => new CreateWorkOrderTaskInput(
                t.TaskType,
                t.Description,
                t.From,
                t.To,
                returnToRamp,
                t.EmployeeIds ?? Array.Empty<Guid>(),
                t.ToolIds ?? Array.Empty<Guid>(),
                t.MaterialIds ?? Array.Empty<Guid>(),
                t.GeneralSupportIds ?? Array.Empty<Guid>(),
                (t.Attachments ?? Array.Empty<MobileTaskAttachmentInput>())
                    .Select(a => new CreateWorkOrderTaskAttachmentInput(
                        a.Kind,
                        a.ContentType,
                        a.FileName,
                        Convert.FromBase64String(a.Base64),
                        a.CapturedAt))
                    .ToList()))
            .ToList();

    private static byte[]? TryDecodeSignature(string? base64Png)
    {
        if (string.IsNullOrWhiteSpace(base64Png))
            return null;

        var payload = base64Png.Trim();
        const string dataUrlPrefix = "base64,";
        var commaIndex = payload.IndexOf(dataUrlPrefix, StringComparison.OrdinalIgnoreCase);
        if (commaIndex >= 0)
            payload = payload[(commaIndex + dataUrlPrefix.Length)..];

        try
        {
            var bytes = Convert.FromBase64String(payload);
            return bytes.Length == 0 ? null : bytes;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>Slim "who am I?" payload for the mobile post-login bootstrap.</summary>
public sealed record MobileMeDto(
    Guid EmployeeId,
    string FullName,
    Guid StationId,
    string StationCode,
    string StationName,
    Guid ManpowerTypeId,
    string ManpowerTypeName);

public sealed record MobileWorkOrderServiceLineInput(
    Guid ServiceId,
    Guid EmployeeId,
    DateTimeOffset From,
    DateTimeOffset To,
    string? Description);

/// <summary>Mobile task payload — replaces the old employee-line / corrective-action shapes.</summary>
public sealed record MobileWorkOrderTaskInput(
    TaskType TaskType,
    string? Description,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<Guid>? EmployeeIds,
    IReadOnlyList<Guid>? ToolIds,
    IReadOnlyList<Guid>? MaterialIds,
    IReadOnlyList<Guid>? GeneralSupportIds,
    IReadOnlyList<MobileTaskAttachmentInput>? Attachments);

/// <summary>Mobile task attachment payload — Base64 bytes plus metadata.</summary>
public sealed record MobileTaskAttachmentInput(
    TaskAttachmentKind Kind,
    string ContentType,
    string FileName,
    string Base64,
    DateTimeOffset CapturedAt);

/// <summary>
/// Body for <c>POST /api/mobile/v2/flights/{flightId}/work-orders</c>. <see cref="ClientMutationId"/>
/// is required when the request originates from the mobile outbox so the server can dedupe
/// retries — see <c>CreateWorkOrderForFlightCommand.ClientMutationId</c>. Older v1 callers
/// (commented out today) didn't include it; nullable here so both shapes deserialise.
/// </summary>
public sealed record MobileCreateWorkOrderRequest(
    string FlightNumber,
    Guid? AircraftTypeId,
    string? AircraftTailNumber,
    DateTimeOffset? Ata,
    DateTimeOffset? Atd,
    string? Remarks,
    IReadOnlyList<MobileWorkOrderServiceLineInput>? ServiceLines,
    IReadOnlyList<MobileWorkOrderTaskInput>? Tasks,
    string? CustomerSignaturePng = null,
    Guid? ClientMutationId = null);

public sealed record MobileUpdateWorkOrderRequest(
    string FlightNumber,
    Guid? AircraftTypeId,
    string? AircraftTailNumber,
    DateTimeOffset? Ata,
    DateTimeOffset? Atd,
    string? Remarks,
    IReadOnlyList<MobileWorkOrderServiceLineInput>? ServiceLines,
    IReadOnlyList<MobileWorkOrderTaskInput>? Tasks,
    bool IsCanceled = false,
    DateTimeOffset? CancellationAt = null,
    string? CustomerSignaturePng = null,
    Guid? ClientMutationId = null);

public sealed record MobileReturnToRampRequest(
    IReadOnlyList<MobileWorkOrderServiceLineInput>? ServiceLines,
    IReadOnlyList<MobileWorkOrderTaskInput>? Tasks,
    string? CustomerSignaturePng = null,
    Guid? ClientMutationId = null);

public sealed record MobileInviteRequest(Guid InviteeEmployeeId);

/// <summary>
/// Payload for <c>POST /api/mobile/v2/flights/{id}/invite</c>: the calling employee assigns
/// a batch of station colleagues to a flight in one request. The inviter is server-derived
/// from the JWT, so only the invitee ids travel in the body.
/// </summary>
public sealed record MobileV2InviteRequest(IReadOnlyList<Guid> InviteeEmployeeIds);

/// <summary>
/// Payload for <c>POST /api/mobile/v2/flights/{id}/cancel</c>: files an empty cancel work
/// order with the chosen <c>CanceledAt</c> (defaults to now when omitted). The optional
/// <c>ClientMutationId</c> is the idempotency key the mobile outbox uses to retry safely
/// after ambiguous-timeout failures.
/// </summary>
public sealed record MobileCancelFlightRequest(DateTimeOffset? CanceledAt, Guid? ClientMutationId = null);

/// <summary>
/// Payload for <c>POST /api/mobile/work-orders/scratch</c>: mobile "create from scratch"
/// flight + work order in one shot. <c>OperationType</c> is implicit (AdHoc) and
/// <c>StationId</c> is forced to the caller's station server-side. <c>CustomerId</c>
/// is required so the ad-hoc flight binds to a real airline (the picker is sourced
/// from the bootstrap customer list).
/// </summary>
public sealed record MobileCreateFromScratchRequest(
    Guid CustomerId,
    string FlightNumber,
    Guid? AircraftTypeId,
    string? AircraftTailNumber,
    DateTimeOffset Sta,
    DateTimeOffset Std,
    bool IsCanceled,
    DateTimeOffset? CancellationAt,
    DateTimeOffset? Ata,
    DateTimeOffset? Atd,
    string? Remarks,
    IReadOnlyList<MobileWorkOrderServiceLineInput>? ServiceLines,
    IReadOnlyList<MobileWorkOrderTaskInput>? Tasks,
    string? CustomerSignaturePng = null,
    Guid? ClientMutationId = null,
    Guid? ClientFlightId = null);

/// <summary>
/// Successful response shape from the v2 create-work-order endpoints. <see cref="Id"/> is
/// the work order id; <see cref="FlightId"/> is the flight it's attached to (echoed from
/// the path param for the existing-flight route, returned by the handler for the scratch
/// route). <see cref="Idempotent"/> is <c>true</c> only when the server matched a prior
/// submission by <c>ClientMutationId</c> / <c>ClientFlightId</c> and nothing new was
/// created — the mobile outbox uses this to log retries without surfacing as "new" to
/// the user.
/// </summary>
public sealed record MobileCreateWorkOrderResponse(Guid Id, Guid FlightId, bool Idempotent);

internal static class MobileResultExtensions
{
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : ToError(result.Error);

    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null)
    {
        if (!result.IsSuccess)
            return ToError(result.Error);

        return onSuccess?.Invoke(result.Value!) ?? Results.Ok(result.Value);
    }

    private static IResult ToError(Error error) => error.Type switch
    {
        ErrorType.NotFound => Results.NotFound(new { error.Code, error.Description }),
        ErrorType.Validation => Results.BadRequest(new { error.Code, error.Description }),
        ErrorType.Conflict => Results.Conflict(new { error.Code, error.Description }),
        ErrorType.Unauthorized => Results.Unauthorized(),
        _ => Results.BadRequest(new { error.Code, error.Description })
    };
}
