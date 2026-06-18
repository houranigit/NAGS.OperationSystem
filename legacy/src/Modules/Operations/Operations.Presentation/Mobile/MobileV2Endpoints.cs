using Core.Contracts.Readers;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Operations.Application.Features.Flight.Commands.CancelFlight;
using Operations.Application.Features.Flight.Commands.CreateFromScratch;
using Operations.Application.Features.Flight.Commands.InviteEmployeesToFlight;
using Operations.Application.Features.Flight.Queries.GetFlightSummaryForMobile;
using Operations.Application.Features.Flight.Queries.GetMyAssignedFlightsForMobile;
using Operations.Application.Features.Flight.Queries.GetMyStationAdHocFlights;
using Operations.Application.Features.Flight.Queries.GetMyStationAogFlights;
using Operations.Application.Features.Mobile.Queries.GetMobileLookups;
using Operations.Application.Features.WorkOrder.Commands.CreateWorkOrderForFlight;
using Operations.Application.Features.WorkOrder.Commands.RecordReturnToRampLines;
using Operations.Application.Features.WorkOrder.Commands.UpdateWorkOrder;
using Operations.Contracts.Mobile;
using Operations.Domain.Aggregates.WorkOrder;
using Operations.Domain.Enumerations;

namespace Operations.Presentation.Mobile;

/// <summary>
/// Minimal API surface for the rebuilt Android client (v2). All routes live under
/// <c>/api/mobile/v2</c> and are bearer-only via the <c>MobileJwt</c> policy. The
/// calling employee is resolved through <see cref="IMobileEmployeeContext"/> so
/// query filters (station, employee id) are server-derived — the client cannot
/// pass them. Legacy v1 lives in <see cref="MobileEndpoints"/> and is currently
/// unmapped.
/// </summary>
public static class MobileV2Endpoints
{
    public static IEndpointRouteBuilder MapMobileV2Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/mobile/v2")
            .WithTags("Mobile V2")
            .RequireAuthorization("MobileJwt");

        group.MapGet("/me", GetMeAsync);
        group.MapGet("/catalogs", GetCatalogsAsync);
        group.MapGet("/employees/at-my-station", GetMyStationEmployeesAsync);
        group.MapGet("/flights/my", GetMyFlightsAsync);
        group.MapGet("/flights/aog", GetMyStationAogFlightsAsync);
        group.MapGet("/flights/ad-hoc", GetMyStationAdHocFlightsAsync);
        // Single-row fetch used by the real-time sync apply path — when the server
        // pushes an `upsert` envelope the mobile client calls this to project just
        // the affected flight in the same shape as `/flights/my`.
        group.MapGet("/flights/{id:guid}", GetFlightByIdAsync);

        // Mobile outbox write surface. Both POSTs accept a client-generated
        // ClientMutationId (and ClientFlightId for the scratch route) so the mobile
        // outbox can retry safely after ambiguous-timeout failures — the handlers
        // short-circuit on duplicate keys via filtered-unique indexes. The success
        // envelope carries `idempotent: true` when nothing new was created.
        // Invite teammates: the calling employee assigns one or more station colleagues to a
        // flight in a single request. Online-only on the client; the inviter is server-derived.
        group.MapPost("/flights/{flightId:guid}/invite", InviteToFlightAsync);

        group.MapPost("/flights/{flightId:guid}/work-orders", CreateWorkOrderForFlightAsync);
        group.MapPost("/flights/{flightId:guid}/cancel", CancelFlightAsync);
        group.MapPost("/work-orders/scratch", CreateWorkOrderFromScratchAsync);
        group.MapPut("/work-orders/{workOrderId:guid}", UpdateWorkOrderAsync);
        group.MapPost("/work-orders/{workOrderId:guid}/return-to-ramp", RecordReturnToRampAsync);

        return app;
    }

    /// <summary>
    /// Returns the signed-in employee's profile (id, full name, station, manpower type).
    /// Drives the post-login "Hello, {name}" header on the mobile client and is also
    /// the side channel through which the mobile sync learns the caller's station id.
    /// </summary>
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

    /// <summary>
    /// Returns the full set of shared lookup catalogs the mobile client caches locally
    /// (services, tools, materials, general supports, customers). No filter, no pagination —
    /// these tables are small and used everywhere, so the mobile sync replaces them in full
    /// on every refresh. Station employees are <em>not</em> included here; the mobile client
    /// fetches them through <c>/employees/at-my-station</c> so each Room table maps to a
    /// single, focused endpoint.
    /// </summary>
    private static async Task<IResult> GetCatalogsAsync(
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        // The endpoint still authenticates against the calling employee so we never serve
        // catalog data to an unlinked / inactive JWT. We pass Guid.Empty to skip the legacy
        // query's roster fetch — that data lives on the dedicated employees endpoint.
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var result = await sender.Send(new GetMobileLookupsQuery(Guid.Empty), ct);
        if (!result.IsSuccess)
            return Results.BadRequest(new { result.Error.Code, result.Error.Description });

        // The AOG seed service is filtered out at the SQL layer inside
        // GetMobileLookupsQueryHandler (via `IServiceReader.ListActiveAsync(excludeAog: true)`),
        // so nothing further is needed here. AOG status still flows through the per-flight
        // `Services` list on `/flights/my` — that's a different concern.
        var lookups = result.Value!;
        return Results.Ok(new MobileV2CatalogsDto(
            lookups.Services,
            lookups.Tools,
            lookups.Materials,
            lookups.GeneralSupports,
            lookups.Customers,
            lookups.AircraftTypes,
            lookups.GeneratedAt));
    }

    /// <summary>
    /// Every active employee at the calling user's home station. Mobile mirrors this list
    /// into its local <c>employees</c> table so teammate / chip pickers stay populated
    /// while offline. Capped at 500 — comfortably above the largest real station.
    /// </summary>
    private static async Task<IResult> GetMyStationEmployeesAsync(
        IMobileEmployeeContext employeeContext,
        IEmployeeReader employeeReader,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var matches = await employeeReader.SearchActiveSnapshotsByStationAsync(
            me.StationSnapshot.StationId,
            search: null,
            take: 500,
            ct);
        return Results.Ok(matches);
    }

    /// <summary>
    /// Non-AOG flights the caller is rostered on, inside a ±12-hour STA window around
    /// <c>UtcNow</c>. The underlying paginated query is asked for a single 100-row page
    /// so the mobile sync gets the full list in one call.
    /// </summary>
    private static async Task<IResult> GetMyFlightsAsync(
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var result = await sender.Send(
            new GetMyAssignedFlightsForMobileQuery(
                me.EmployeeId,
                Page: 1,
                PageSize: 100,
                Search: null,
                WindowHours: 12,
                Status: null,
                IncludeAog: false),
            ct);
        if (!result.IsSuccess)
            return Results.BadRequest(new { result.Error.Code, result.Error.Description });

        // The mobile sync only cares about the rows; the surrounding pagination envelope is a
        // remnant of the legacy paged UI tab. Expose the flat list so v2 clients can replace
        // their local table in one Room transaction.
        return Results.Ok(result.Value!.Items);
    }

    /// <summary>
    /// AOG flights at the caller's current station (whether they are rostered or not),
    /// inside a ±12-hour STA window around <c>UtcNow</c>.
    /// </summary>
    private static async Task<IResult> GetMyStationAogFlightsAsync(
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var result = await sender.Send(new GetMyStationAogFlightsQuery(me.EmployeeId, WindowHours: 12), ct);
        if (!result.IsSuccess)
            return Results.BadRequest(new { result.Error.Code, result.Error.Description });

        return Results.Ok(result.Value!);
    }

    /// <summary>
    /// Ad Hoc operation-type flights at the caller's current station (Scheduled / InProgress),
    /// inside a ±12-hour STA window around <c>UtcNow</c>.
    /// </summary>
    private static async Task<IResult> GetMyStationAdHocFlightsAsync(
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var result = await sender.Send(new GetMyStationAdHocFlightsQuery(me.EmployeeId, WindowHours: 12), ct);
        if (!result.IsSuccess)
            return Results.BadRequest(new { result.Error.Code, result.Error.Description });

        return Results.Ok(result.Value!);
    }

    /// <summary>
    /// Returns the same <see cref="MobileFlightSummaryDto"/> projection as <c>/flights/my</c>
    /// for a single id. Used by the mobile real-time sync: when an <c>upsert</c> envelope
    /// arrives over SignalR, the client fetches just that flight here and upserts it into
    /// its local Room table. Scoped to the caller via <see cref="IMobileEmployeeContext"/>
    /// so the <c>MyWorkOrder</c> payload reflects this employee's under-review draft and
    /// nobody else's work order.
    /// </summary>
    /// <remarks>
    /// We deliberately don't enforce a station or assignment filter here — the per-row
    /// fetch must succeed even for AOG flights at the caller's station that they're not
    /// assigned to (otherwise the AOG tab can't ever reconcile via push). The returned
    /// row is still safe to share: it contains no PII beyond what the list endpoints
    /// already surface to the same caller.
    /// </remarks>
    private static async Task<IResult> GetFlightByIdAsync(
        Guid id,
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var result = await sender.Send(new GetFlightSummaryForMobileQuery(id, me.EmployeeId), ct);
        if (!result.IsSuccess)
            return Results.BadRequest(new { result.Error.Code, result.Error.Description });

        if (result.Value is null)
            return Results.NotFound();

        return Results.Ok(result.Value);
    }

    /// <summary>
    /// Assigns one or more station colleagues to a flight in a single call. The inviter is the
    /// calling employee (resolved from the JWT, never the request body); invitee ids come in the
    /// body as a list. The handler is idempotent — already-assigned employees and a self-invite
    /// are skipped server-side — and processes the whole batch with a single flight load + save.
    /// </summary>
    private static async Task<IResult> InviteToFlightAsync(
        Guid flightId,
        MobileV2InviteRequest request,
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var command = new InviteEmployeesToFlightCommand(
            flightId,
            request.InviteeEmployeeIds ?? Array.Empty<Guid>(),
            me.EmployeeId);
        var result = await sender.Send(command, ct);
        return result.IsSuccess ? Results.NoContent() : ToError(result.Error);
    }

    /// <summary>
    /// Mobile outbox path for "create work order on an existing flight". The calling
    /// employee is resolved from the JWT and recorded as <c>CreatedByEmployeeId</c> on the
    /// work order — client cannot impersonate. The optional
    /// <see cref="MobileCreateWorkOrderRequest.ClientMutationId"/> is the idempotency key
    /// the mobile outbox uses to retry safely after ambiguous network failures: when
    /// supplied and a prior work order matches, the handler returns the existing ids and
    /// the response carries <c>idempotent: true</c>.
    /// </summary>
    private static async Task<IResult> CreateWorkOrderForFlightAsync(
        Guid flightId,
        MobileCreateWorkOrderRequest request,
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var services = MapServiceLines(request.ServiceLines);
        var tasks = MapTasks(request.Tasks);

        var command = new CreateWorkOrderForFlightCommand(
            FlightId: flightId,
            FlightNumber: request.FlightNumber,
            AircraftTypeId: request.AircraftTypeId,
            AircraftTailNumber: request.AircraftTailNumber,
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
        if (!result.IsSuccess)
            return ToError(result.Error);

        var payload = result.Value!;
        return Results.Created(
            $"/api/mobile/v2/work-orders/{payload.WorkOrderId}",
            new MobileCreateWorkOrderResponse(payload.WorkOrderId, payload.FlightId, payload.Idempotent));
    }

    /// <summary>
    /// Mobile outbox path for "cancel a flight". Files an empty cancel work order with the
    /// supplied <see cref="MobileCancelFlightRequest.CanceledAt"/> (defaults to now), reusing
    /// the same <see cref="CancelFlightCommand"/> the web portal uses. The calling employee is
    /// resolved from the JWT. The optional <see cref="MobileCancelFlightRequest.ClientMutationId"/>
    /// makes outbox retries duplicate-safe — when a cancel work order already exists for that
    /// key the handler returns the existing id.
    /// </summary>
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

        var command = new CancelFlightCommand(
            FlightId: flightId,
            CanceledAt: request?.CanceledAt ?? DateTimeOffset.UtcNow,
            CreatedByEmployeeId: me.EmployeeId,
            ClientMutationId: request?.ClientMutationId);
        var result = await sender.Send(command, ct);
        if (!result.IsSuccess)
            return ToError(result.Error);

        var workOrderId = result.Value;
        return Results.Created(
            $"/api/mobile/v2/work-orders/{workOrderId}",
            new MobileCreateWorkOrderResponse(workOrderId, flightId, Idempotent: false));
    }

    /// <summary>
    /// Mobile outbox path for "create an ad-hoc flight + work order from scratch". The
    /// flight's station is forced to the caller's station server-side, and the operation
    /// type is always AdHoc — the client cannot override either. The optional
    /// <see cref="MobileCreateFromScratchRequest.ClientFlightId"/> lets the mobile client
    /// pre-allocate a stable id while offline so a retry cannot create two flights; the
    /// optional <see cref="MobileCreateFromScratchRequest.ClientMutationId"/> guards the
    /// work order half. Either match → handler returns existing ids with
    /// <c>idempotent: true</c>.
    /// </summary>
    private static async Task<IResult> CreateWorkOrderFromScratchAsync(
        MobileCreateFromScratchRequest request,
        IMobileEmployeeContext employeeContext,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var services = MapServiceLines(request.ServiceLines);
        var tasks = MapTasks(request.Tasks);

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
        if (!result.IsSuccess)
            return ToError(result.Error);

        var payload = result.Value!;
        return Results.Created(
            $"/api/mobile/v2/work-orders/{payload.WorkOrderId}",
            new MobileCreateWorkOrderResponse(payload.WorkOrderId, payload.FlightId, payload.Idempotent));
    }

    /// <summary>
    /// Edits an existing under-review work order (mobile outbox uses PUT, never POST create).
    /// The caller must be the work order's <c>CreatedByEmployeeId</c> — the server rejects
    /// cross-employee edits. Optional <see cref="MobileUpdateWorkOrderRequest.ClientMutationId"/>
    /// is echoed on flight sync pushes so the device can correlate with its outbox row.
    /// </summary>
    private static async Task<IResult> UpdateWorkOrderAsync(
        Guid workOrderId,
        MobileUpdateWorkOrderRequest request,
        IMobileEmployeeContext employeeContext,
        IWorkOrderRepository workOrders,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var existing = await workOrders.GetByIdAsync(WorkOrderId.From(workOrderId), ct);
        if (existing is null)
            return Results.NotFound(new { code = "NotFound", description = "Work order not found." });

        if (existing.CreatedByEmployeeId != me.EmployeeId)
        {
            return Results.Json(
                new
                {
                    title = "Forbidden",
                    detail = "Only the employee who created this work order may update it from mobile.",
                    code = "Mobile.WorkOrderUpdateNotOwned",
                    status = StatusCodes.Status403Forbidden
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var services = MapServiceLines(request.ServiceLines);
        var tasks = MapTasks(request.Tasks);

        var command = new UpdateWorkOrderCommand(
            workOrderId,
            request.FlightNumber,
            request.AircraftTypeId,
            request.AircraftTailNumber,
            IsCanceled: request.IsCanceled,
            CancellationAt: request.CancellationAt,
            Ata: request.Ata,
            Atd: request.Atd,
            ServiceLines: services,
            Tasks: tasks,
            Remarks: request.Remarks,
            CustomerSignature: TryDecodeSignature(request.CustomerSignaturePng),
            ClientMutationId: request.ClientMutationId);
        var result = await sender.Send(command, ct);
        return result.IsSuccess ? Results.NoContent() : ToError(result.Error);
    }

    /// <summary>
    /// Append return-to-ramp service lines and tasks to an under-review work order.
    /// Ownership matches <see cref="UpdateWorkOrderAsync"/>; lines are stored with
    /// <c>ReturnToRamp = true</c>.
    /// </summary>
    private static async Task<IResult> RecordReturnToRampAsync(
        Guid workOrderId,
        MobileReturnToRampRequest request,
        IMobileEmployeeContext employeeContext,
        IWorkOrderRepository workOrders,
        ISender sender,
        CancellationToken ct)
    {
        var me = await employeeContext.GetCurrentEmployeeAsync(ct);
        if (me is null)
            return NoEmployeeLinkedProfile();

        var existing = await workOrders.GetByIdAsync(WorkOrderId.From(workOrderId), ct);
        if (existing is null)
            return Results.NotFound(new { code = "NotFound", description = "Work order not found." });

        if (existing.CreatedByEmployeeId != me.EmployeeId)
        {
            return Results.Json(
                new
                {
                    title = "Forbidden",
                    detail = "Only the employee who created this work order may add return-to-ramp lines from mobile.",
                    code = "Mobile.WorkOrderReturnToRampNotOwned",
                    status = StatusCodes.Status403Forbidden
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var services = MapServiceLines(request.ServiceLines, returnToRamp: true);
        var tasks = MapTasks(request.Tasks, returnToRamp: true);

        var command = new RecordReturnToRampLinesCommand(
            workOrderId,
            services,
            tasks,
            CustomerSignature: TryDecodeSignature(request.CustomerSignaturePng),
            ClientMutationId: request.ClientMutationId);
        var result = await sender.Send(command, ct);
        return result.IsSuccess ? Results.NoContent() : ToError(result.Error);
    }

    /// <summary>
    /// Maps mobile service-line inputs to the application command shape. The
    /// <c>ReturnToRamp</c> flag is <c>false</c> for create/update outbox paths —
    /// RTR uses <see cref="RecordReturnToRampAsync"/>.
    /// </summary>
    private static List<CreateWorkOrderServiceLineInput> MapServiceLines(
        IReadOnlyList<MobileWorkOrderServiceLineInput>? source,
        bool returnToRamp = false) =>
        (source ?? Array.Empty<MobileWorkOrderServiceLineInput>())
            .Select(s => new CreateWorkOrderServiceLineInput(
                s.ServiceId, s.EmployeeId, s.From, s.To, s.Description, returnToRamp))
            .ToList();

    /// <summary>
    /// Maps mobile task inputs to the application command shape, decoding Base64
    /// attachment payloads into <c>byte[]</c>. Invalid Base64 in a single attachment
    /// blows up the whole request — that's intentional: the mobile outbox would otherwise
    /// retry forever against a payload the server cannot decode.
    /// </summary>
    private static List<CreateWorkOrderTaskInput> MapTasks(
        IReadOnlyList<MobileWorkOrderTaskInput>? source,
        bool returnToRamp = false) =>
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

    /// <summary>
    /// Decodes a Base64 PNG signature, tolerating an optional <c>data:image/png;base64,</c>
    /// prefix. Returns null on empty / invalid payloads so the work order persists without
    /// a signature rather than failing the whole submission.
    /// </summary>
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

    /// <summary>
    /// Maps a domain <see cref="BuildingBlocks.Domain.Results.Error"/> to the HTTP shape
    /// every v2 endpoint uses. Mirrors <c>MobileResultExtensions.ToError</c> on the v1
    /// surface so the mobile client has one error contract across both bases.
    /// </summary>
    private static IResult ToError(BuildingBlocks.Domain.Results.Error error) => error.Type switch
    {
        BuildingBlocks.Domain.Results.ErrorType.NotFound => Results.NotFound(new { error.Code, error.Description }),
        BuildingBlocks.Domain.Results.ErrorType.Validation => Results.BadRequest(new { error.Code, error.Description }),
        BuildingBlocks.Domain.Results.ErrorType.Conflict => Results.Conflict(new { error.Code, error.Description }),
        BuildingBlocks.Domain.Results.ErrorType.Unauthorized => Results.Unauthorized(),
        _ => Results.BadRequest(new { error.Code, error.Description })
    };

    /// <summary>
    /// Same 403 shape every mobile endpoint returns when the JWT subject is valid but the
    /// account has no linked employee record — the mobile client surfaces the <c>detail</c>
    /// text verbatim so the support contact can act on it.
    /// </summary>
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
}
