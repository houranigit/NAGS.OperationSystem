using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Contracts.Seeding;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Features.Flights;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Operations.Domain.Mobile;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Operations.Application.Features.Mobile;

/// <summary>
/// Result of a mobile write. <see cref="Idempotent"/> is true when the request replayed a mutation
/// the server had already applied (the client retried after losing the first response).
/// </summary>
public sealed record MobileWriteResultDto(Guid WorkOrderId, Guid FlightId, bool Idempotent);

/// <summary>
/// Shared idempotency plumbing for the mobile write commands. The mutation record is added to the
/// same scoped DbContext the inner command saves through, so the business change and the
/// idempotency record commit atomically — a replayed <c>clientMutationId</c> is answered from the
/// record instead of duplicating the write.
/// </summary>
internal static class MobileMutations
{
    private static readonly JsonSerializerOptions PreReturnToRampFingerprintOptions =
        CreatePreReturnToRampFingerprintOptions();
    private static readonly JsonSerializerOptions PreServiceLineAttachmentsFingerprintOptions =
        CreatePreServiceLineAttachmentsFingerprintOptions();
    private static readonly JsonSerializerOptions LegacySinglePerformerFingerprintOptions =
        CreateLegacySinglePerformerFingerprintOptions(removeProvenance: false);
    private static readonly JsonSerializerOptions LegacyPreReturnToRampFingerprintOptions =
        CreateLegacySinglePerformerFingerprintOptions(removeProvenance: true);

    public static bool IsCanonicalClientMutationId(string? value) =>
        Guid.TryParseExact(value, "D", out var parsed) &&
        string.Equals(parsed.ToString(), value, StringComparison.Ordinal);

    public static string Fingerprint<T>(T request) => Fingerprint(request, options: null);

    /// <summary>
    /// Reproduces fingerprints written before service-line identity and return-to-ramp provenance
    /// were added to the mobile command models. Deployment-spanning retries can therefore match
    /// their existing mutation record while all newly stored fingerprints retain the full schema.
    /// </summary>
    public static string PreReturnToRampFingerprint<T>(T request) =>
        Fingerprint(request, PreReturnToRampFingerprintOptions);

    /// <summary>
    /// Produces every fingerprint shape that the immediately preceding mobile contracts could
    /// have persisted. In addition to the provenance-free shape, single-performer service lines
    /// are projected back to the former singular property so an in-flight retry remains
    /// idempotent across the performer-collection deployment.
    /// </summary>
    public static IReadOnlyList<string> CompatibleFingerprints<T>(T request)
    {
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        if (HasInlineServiceLineAttachments(request))
            return fingerprints.ToList();

        fingerprints.Add(Fingerprint(request, PreServiceLineAttachmentsFingerprintOptions));
        fingerprints.Add(PreReturnToRampFingerprint(request));

        AddLegacySinglePerformerFingerprint(
            request,
            LegacySinglePerformerFingerprintOptions,
            fingerprints);
        AddLegacySinglePerformerFingerprint(
            request,
            LegacyPreReturnToRampFingerprintOptions,
            fingerprints);

        return fingerprints.ToList();
    }

    private static bool HasInlineServiceLineAttachments<T>(T request)
    {
        var root = JsonSerializer.SerializeToElement(request);
        return ContainsServiceLineAttachments(root);
    }

    private static bool ContainsServiceLineAttachments(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(nameof(WorkOrderServiceLineCommand.ServiceId), out _) &&
                element.TryGetProperty(nameof(WorkOrderServiceLineCommand.Attachments), out var attachments) &&
                attachments.ValueKind == JsonValueKind.Array &&
                attachments.GetArrayLength() > 0)
            {
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (ContainsServiceLineAttachments(property.Value))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsServiceLineAttachments(item))
                    return true;
            }
        }

        return false;
    }

    private static string Fingerprint<T>(T request, JsonSerializerOptions? options)
    {
        var json = JsonSerializer.Serialize(request, options);
        return FingerprintJson(json);
    }

    private static string FingerprintJson(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private static void AddLegacySinglePerformerFingerprint<T>(
        T request,
        JsonSerializerOptions options,
        ISet<string> fingerprints)
    {
        try
        {
            fingerprints.Add(Fingerprint(request, options));
        }
        catch (JsonException)
        {
            // A legacy request could only represent one performer. A line containing zero or
            // multiple performers therefore has no truthful singular-contract fingerprint.
        }
    }

    private static JsonSerializerOptions CreatePreReturnToRampFingerprintOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(static typeInfo =>
        {
            // The identity-awareness marker was added to the update fingerprint envelope with the
            // line ids. It did not exist in mutation records written by the previous deployment.
            RemoveProperty(typeInfo, "ServiceLineIdentityVersion");

            if (typeInfo.Type == typeof(WorkOrderServiceLineCommand))
            {
                RemoveProperty(typeInfo, "Id");
                RemoveProperty(typeInfo, nameof(WorkOrderServiceLineCommand.IsReturnToRamp));
                RemoveProperty(typeInfo, nameof(WorkOrderServiceLineCommand.Attachments));
            }
            else if (typeInfo.Type == typeof(WorkOrderTaskCommand))
            {
                RemoveProperty(typeInfo, nameof(WorkOrderTaskCommand.IsReturnToRamp));
            }
        });

        return new JsonSerializerOptions { TypeInfoResolver = resolver };
    }

    private static JsonSerializerOptions CreatePreServiceLineAttachmentsFingerprintOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(static typeInfo =>
        {
            if (typeInfo.Type == typeof(WorkOrderServiceLineCommand))
                RemoveProperty(typeInfo, nameof(WorkOrderServiceLineCommand.Attachments));
        });

        return new JsonSerializerOptions { TypeInfoResolver = resolver };
    }

    private static JsonSerializerOptions CreateLegacySinglePerformerFingerprintOptions(
        bool removeProvenance)
    {
        var options = removeProvenance
            ? CreatePreReturnToRampFingerprintOptions()
            : new JsonSerializerOptions();
        options.Converters.Add(new LegacySinglePerformerCommandConverter(removeProvenance));
        return options;
    }

    private sealed class LegacySinglePerformerCommandConverter(bool removeProvenance)
        : JsonConverter<WorkOrderServiceLineCommand>
    {
        public override WorkOrderServiceLineCommand Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(
            Utf8JsonWriter writer,
            WorkOrderServiceLineCommand value,
            JsonSerializerOptions options)
        {
            if (value.PerformedByStaffMemberIds is not { Count: 1 })
                throw new JsonException("The singular performer contract requires exactly one performer.");

            writer.WriteStartObject();
            writer.WriteString(nameof(value.ServiceId), value.ServiceId);
            writer.WriteString("PerformedByStaffMemberId", value.PerformedByStaffMemberIds[0]);
            writer.WriteString(nameof(value.FromUtc), value.FromUtc);
            writer.WriteString(nameof(value.ToUtc), value.ToUtc);
            if (value.Description is null)
                writer.WriteNull(nameof(value.Description));
            else
                writer.WriteString(nameof(value.Description), value.Description);

            if (!removeProvenance)
            {
                writer.WriteBoolean(nameof(value.IsReturnToRamp), value.IsReturnToRamp);
                if (value.Id is { } id)
                    writer.WriteString(nameof(value.Id), id);
                else
                    writer.WriteNull(nameof(value.Id));
            }

            writer.WriteEndObject();
        }
    }

    private static void RemoveProperty(JsonTypeInfo typeInfo, string propertyName)
    {
        for (var index = typeInfo.Properties.Count - 1; index >= 0; index--)
        {
            if (string.Equals(typeInfo.Properties[index].Name, propertyName, StringComparison.Ordinal))
                typeInfo.Properties.RemoveAt(index);
        }
    }

    public static async Task<Result<MobileMutation?>> FindReplayAsync(
        IOperationsDbContext db,
        string clientMutationId,
        Guid ownerUserId,
        string expectedKind,
        string requestFingerprint,
        Guid? expectedWorkOrderId,
        Guid? expectedFlightId,
        Guid? expectedClientFlightId,
        CancellationToken ct,
        IReadOnlyCollection<string>? compatibleRequestFingerprints = null)
    {
        var mutation = await db.MobileMutations.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ClientMutationId == clientMutationId, ct);
        if (mutation is null)
            return Result.Success<MobileMutation?>(null);

        var targetMismatch =
            (expectedWorkOrderId is not null && mutation.WorkOrderId != expectedWorkOrderId) ||
            (expectedFlightId is not null && mutation.FlightId != expectedFlightId) ||
            (expectedClientFlightId is not null && mutation.ClientFlightId != expectedClientFlightId);
        var fingerprintMismatch = !string.IsNullOrWhiteSpace(mutation.RequestFingerprint) &&
            !string.Equals(mutation.RequestFingerprint, requestFingerprint, StringComparison.Ordinal) &&
            !(compatibleRequestFingerprints?.Contains(mutation.RequestFingerprint, StringComparer.Ordinal) ?? false);

        if (mutation.OwnerUserId != ownerUserId ||
            !string.Equals(mutation.Kind, expectedKind, StringComparison.Ordinal) ||
            targetMismatch ||
            fingerprintMismatch)
        {
            return Error.Conflict(
                "This client mutation id was already used for a different request.",
                "Operations.Mobile.MutationKeyReused");
        }

        return Result.Success<MobileMutation?>(mutation);
    }

    public static async Task<Result<MobileWriteResultDto>> ReplayAsync(
        IOperationsDbContext db, MobileMutation mutation, CancellationToken ct)
    {
        if (mutation.WorkOrderId is { } workOrderId)
        {
            var flightId = mutation.FlightId
                ?? await db.WorkOrders.AsNoTracking()
                    .Where(w => w.Id == workOrderId)
                    .Select(w => (Guid?)w.FlightId)
                    .FirstOrDefaultAsync(ct);

            return new MobileWriteResultDto(workOrderId, flightId ?? Guid.Empty, Idempotent: true);
        }

        return new MobileWriteResultDto(Guid.Empty, mutation.FlightId ?? Guid.Empty, Idempotent: true);
    }
}

/// <summary>
/// Enforces the same fixed STA window as the mobile lists and action sheet. The client also gates
/// its controls, but this server-side check prevents a stale cache or an older app build from
/// mutating a flight that is currently information-only.
/// </summary>
internal static class MobileActionWindow
{
    public static async Task<Result> EnsureAvailableAsync(
        IOperationsDbContext db,
        Guid flightId,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var scheduledArrivalUtc = await db.Flights.AsNoTracking()
            .Where(flight => flight.Id == flightId)
            .Select(flight => (DateTimeOffset?)flight.Schedule.Sta)
            .FirstOrDefaultAsync(cancellationToken);
        if (scheduledArrivalUtc is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        return MobileFlightWindow.Evaluate(scheduledArrivalUtc.Value, timeProvider.GetUtcNow()).IsWithinWindow
            ? Result.Success()
            : Error.Forbidden(
                "This flight is outside the mobile action window and is available for information only.",
                "Operations.Mobile.FlightOutsideActionWindow");
    }
}

// --- Submit a work order for an existing flight ---------------------------------------

public sealed record MobileSubmitWorkOrderCommand(
    Guid FlightId,
    WorkOrderType Type,
    WorkOrderEditableCommandPayload Payload,
    string ClientMutationId) : ICommand<MobileWriteResultDto>;

public sealed class MobileSubmitWorkOrderCommandValidator : AbstractValidator<MobileSubmitWorkOrderCommand>
{
    public MobileSubmitWorkOrderCommandValidator()
    {
        RuleFor(x => x.FlightId).NotEmpty();
        RuleFor(x => x.Payload).NotNull();
        RuleFor(x => x.ClientMutationId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(MobileMutations.IsCanonicalClientMutationId)
            .WithMessage("Client mutation id must be a canonical UUID.");
    }
}

public sealed class MobileSubmitWorkOrderCommandHandler(
    IOperationsDbContext db,
    ISender sender,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<MobileSubmitWorkOrderCommand, MobileWriteResultDto>
{
    public async Task<Result<MobileWriteResultDto>> Handle(MobileSubmitWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        const string mutationKind = "submit-work-order";
        var fingerprintInput = new { request.FlightId, request.Type, request.Payload };
        var fingerprint = MobileMutations.Fingerprint(fingerprintInput);
        var compatibleFingerprints = MobileMutations.CompatibleFingerprints(fingerprintInput);
        var replay = await MobileMutations.FindReplayAsync(
            db, request.ClientMutationId, userId, mutationKind, fingerprint,
            expectedWorkOrderId: null, expectedFlightId: request.FlightId, expectedClientFlightId: null,
            cancellationToken, compatibleFingerprints);
        if (replay.IsFailure)
            return replay.Error;
        if (replay.Value is { } prior)
            return await MobileMutations.ReplayAsync(db, prior, cancellationToken);

        var actionWindow = await MobileActionWindow.EnsureAvailableAsync(
            db, request.FlightId, timeProvider, cancellationToken);
        if (actionWindow.IsFailure)
            return actionWindow.Error;

        // Pre-generate the work order id and stage the mutation record so the inner command's
        // SaveChanges persists both atomically.
        var workOrderId = Guid.NewGuid();
        db.MobileMutations.Add(MobileMutation.Record(
            request.ClientMutationId, userId, mutationKind,
            workOrderId, request.FlightId, clientFlightId: null, fingerprint, timeProvider.GetUtcNow()));

        var result = await sender.Send(
            new SubmitWorkOrderCommand(
                request.FlightId,
                request.Type,
                MobileReturnToRampProvenance.ProtectNew(request.Payload),
                request.ClientMutationId,
                workOrderId),
            cancellationToken);

        if (result.IsFailure)
            return result.Error;

        return new MobileWriteResultDto(result.Value, request.FlightId, Idempotent: false);
    }
}

// --- Create an ad-hoc flight + work order from scratch --------------------------------

public sealed record MobileCreateScratchWorkOrderCommand(
    Guid? CustomerId,
    string FlightNumber,
    DateTimeOffset ScheduledArrivalUtc,
    DateTimeOffset ScheduledDepartureUtc,
    Guid? AircraftTypeId,
    IReadOnlyList<Guid> PlannedServiceIds,
    WorkOrderType Type,
    WorkOrderEditableCommandPayload Payload,
    string ClientMutationId,
    Guid ClientFlightId) : ICommand<MobileWriteResultDto>;

public sealed class MobileCreateScratchWorkOrderCommandValidator : AbstractValidator<MobileCreateScratchWorkOrderCommand>
{
    public MobileCreateScratchWorkOrderCommandValidator()
    {
        RuleFor(x => x.FlightNumber).NotEmpty().MaximumLength(12);
        RuleFor(x => x.Payload).NotNull();
        RuleFor(x => x.Payload.Remarks)
            .NotEmpty()
            .WithMessage("Remarks are required when the customer is unknown.")
            .When(x =>
                x.Payload is not null &&
                (x.CustomerId is null ||
                 x.CustomerId == Guid.Empty ||
                 x.CustomerId == WellKnownMasterDataIds.UnknownCustomer));
        RuleFor(x => x.ClientMutationId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(MobileMutations.IsCanonicalClientMutationId)
            .WithMessage("Client mutation id must be a canonical UUID.");
        RuleFor(x => x.ClientFlightId).NotEmpty();
    }
}

public sealed class MobileCreateScratchWorkOrderCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    ISender sender,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<MobileCreateScratchWorkOrderCommand, MobileWriteResultDto>
{
    public async Task<Result<MobileWriteResultDto>> Handle(MobileCreateScratchWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        var customerId = request.CustomerId is { } requestedCustomerId && requestedCustomerId != Guid.Empty
            ? requestedCustomerId
            : WellKnownMasterDataIds.UnknownCustomer;

        const string mutationKind = "scratch-work-order";
        var fingerprintInput = new
        {
            CustomerId = customerId,
            request.FlightNumber,
            request.ScheduledArrivalUtc,
            request.ScheduledDepartureUtc,
            request.AircraftTypeId,
            request.PlannedServiceIds,
            request.Type,
            request.Payload,
            request.ClientFlightId
        };
        var fingerprint = MobileMutations.Fingerprint(fingerprintInput);
        var compatibleFingerprints = MobileMutations.CompatibleFingerprints(fingerprintInput);
        var replay = await MobileMutations.FindReplayAsync(
            db, request.ClientMutationId, userId, mutationKind, fingerprint,
            expectedWorkOrderId: null, expectedFlightId: null, expectedClientFlightId: request.ClientFlightId,
            cancellationToken, compatibleFingerprints);
        if (replay.IsFailure)
            return replay.Error;
        if (replay.Value is { } prior)
            return await MobileMutations.ReplayAsync(db, prior, cancellationToken);

        // A different mutation already materialised this client flight (e.g. the same offline draft
        // submitted twice, or from a second device) — a duplicate scratch flight is a conflict.
        var duplicateFlight = await db.MobileMutations.AsNoTracking()
            .AnyAsync(m => m.ClientFlightId == request.ClientFlightId, cancellationToken);
        if (duplicateFlight)
            return Error.Conflict("This ad-hoc flight was already submitted.", "Operations.Mobile.ScratchFlightDuplicate");

        // The mobile client cannot pick the station; it is forced to the caller's own station.
        var scopeResult = MobileScope.EnsureStationStaff(await scope.ResolveAsync(cancellationToken));
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var stationId = scopeResult.Value.StationId!.Value;

        var flightId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        db.MobileMutations.Add(MobileMutation.Record(
            request.ClientMutationId, userId, mutationKind,
            workOrderId, flightId, request.ClientFlightId, fingerprint, timeProvider.GetUtcNow()));

        var result = await sender.Send(
            new CreateAdHocWorkOrderCommand(
                customerId,
                stationId,
                request.FlightNumber,
                request.ScheduledArrivalUtc,
                request.ScheduledDepartureUtc,
                request.AircraftTypeId,
                request.PlannedServiceIds,
                AssignedStaffMemberIds: [],
                request.Type,
                MobileReturnToRampProvenance.ProtectNew(request.Payload),
                request.ClientMutationId,
                flightId,
                workOrderId),
            cancellationToken);

        if (result.IsFailure)
            return result.Error;

        return new MobileWriteResultDto(result.Value, flightId, Idempotent: false);
    }
}

// --- Update an editable work order ------------------------------------------------------

public sealed record MobileUpdateWorkOrderCommand(
    Guid WorkOrderId,
    WorkOrderType Type,
    WorkOrderEditableCommandPayload Payload,
    string ClientMutationId,
    string BaseRowVersion,
    int ServiceLineIdentityVersion = 0) : ICommand<MobileWriteResultDto>;

public sealed class MobileUpdateWorkOrderCommandValidator : AbstractValidator<MobileUpdateWorkOrderCommand>
{
    public MobileUpdateWorkOrderCommandValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty();
        RuleFor(x => x.Payload).NotNull();
        RuleFor(x => x.ClientMutationId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(MobileMutations.IsCanonicalClientMutationId)
            .WithMessage("Client mutation id must be a canonical UUID.");
    }
}

public sealed class MobileUpdateWorkOrderCommandHandler(
    IOperationsDbContext db,
    ISender sender,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<MobileUpdateWorkOrderCommand, MobileWriteResultDto>
{
    public async Task<Result<MobileWriteResultDto>> Handle(MobileUpdateWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        const string mutationKind = "update-work-order";
        var fingerprintInput = new
        {
            request.WorkOrderId,
            request.Type,
            request.Payload,
            request.BaseRowVersion,
            request.ServiceLineIdentityVersion
        };
        var fingerprint = MobileMutations.Fingerprint(fingerprintInput);
        var compatibleFingerprints = MobileMutations.CompatibleFingerprints(fingerprintInput);
        var replay = await MobileMutations.FindReplayAsync(
            db, request.ClientMutationId, userId, mutationKind, fingerprint,
            expectedWorkOrderId: request.WorkOrderId, expectedFlightId: null, expectedClientFlightId: null,
            cancellationToken, compatibleFingerprints);
        if (replay.IsFailure)
            return replay.Error;
        if (replay.Value is { } prior)
            return await MobileMutations.ReplayAsync(db, prior, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.BaseRowVersion))
        {
            return Error.Validation(
                "The cached base row version is required for an offline update.",
                "Operations.Mobile.BaseRowVersionRequired");
        }

        byte[] baseRowVersion;
        try
        {
            baseRowVersion = Convert.FromBase64String(request.BaseRowVersion);
        }
        catch (FormatException)
        {
            return Error.Validation("The base row version is invalid.", "Operations.Mobile.InvalidBaseRowVersion");
        }

        // Load the server-owned row identities and provenance before accepting a mobile full
        // replacement. Normal mobile updates may preserve an existing RTR row, but only the
        // dedicated return-to-ramp command is allowed to create one.
        var current = await WorkOrderLoader.ForMutation(db.WorkOrders.AsNoTracking())
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);
        if (current is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var protectedPayload = MobileReturnToRampProvenance.ProtectUpdate(
            request.Payload,
            current.ServiceLines.ToDictionary(line => line.Id, line => line.IsReturnToRamp),
            current.Tasks.ToDictionary(task => task.Id, task => task.IsReturnToRamp),
            request.ServiceLineIdentityVersion,
            current.ServiceLines.Any(line => line.Attachments.Count > 0));
        if (protectedPayload.IsFailure)
            return protectedPayload.Error;

        var actionWindow = await MobileActionWindow.EnsureAvailableAsync(
            db, current.FlightId, timeProvider, cancellationToken);
        if (actionWindow.IsFailure)
            return actionWindow.Error;

        db.MobileMutations.Add(MobileMutation.Record(
            request.ClientMutationId, userId, mutationKind,
            request.WorkOrderId, current.FlightId, clientFlightId: null, fingerprint, timeProvider.GetUtcNow()));

        var result = await sender.Send(
            new UpdateWorkOrderCommand(
                request.WorkOrderId,
                baseRowVersion,
                request.Type,
                protectedPayload.Value,
                request.ClientMutationId),
            cancellationToken);

        if (result.IsFailure)
            return result.Error;

        return new MobileWriteResultDto(request.WorkOrderId, current.FlightId, Idempotent: false);
    }
}

// --- Return-to-ramp: append service lines / tasks to an editable work order ----------------

public sealed record MobileReturnToRampCommand(
    Guid WorkOrderId,
    IReadOnlyList<WorkOrderServiceLineCommand> ServiceLines,
    IReadOnlyList<WorkOrderTaskCommand> Tasks,
    string ClientMutationId) : ICommand<MobileWriteResultDto>;

public sealed class MobileReturnToRampCommandValidator : AbstractValidator<MobileReturnToRampCommand>
{
    public MobileReturnToRampCommandValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty();
        RuleFor(x => x.ClientMutationId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(MobileMutations.IsCanonicalClientMutationId)
            .WithMessage("Client mutation id must be a canonical UUID.");
        RuleFor(x => x)
            .Must(x => (x.ServiceLines?.Count ?? 0) + (x.Tasks?.Count ?? 0) > 0)
            .WithMessage("Return to ramp requires at least one service line or task.");
    }
}

/// <summary>
/// The legacy mobile return-to-ramp appended lines to an under-review work order. The v1 model has
/// no separate return-to-ramp record, so append semantics are implemented as a full update: the
/// current lines/tasks are re-sent (tasks keep their ids so attachments survive) with the new rows
/// appended, going through the same update pipeline and rules as any other edit.
/// </summary>
public sealed class MobileReturnToRampCommandHandler(
    IOperationsDbContext db,
    ISender sender,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<MobileReturnToRampCommand, MobileWriteResultDto>
{
    public async Task<Result<MobileWriteResultDto>> Handle(MobileReturnToRampCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        const string mutationKind = "return-to-ramp";
        var fingerprintInput = new
        {
            request.WorkOrderId,
            request.ServiceLines,
            request.Tasks
        };
        var fingerprint = MobileMutations.Fingerprint(fingerprintInput);
        var compatibleFingerprints = MobileMutations.CompatibleFingerprints(fingerprintInput);
        var replay = await MobileMutations.FindReplayAsync(
            db, request.ClientMutationId, userId, mutationKind, fingerprint,
            expectedWorkOrderId: request.WorkOrderId, expectedFlightId: null, expectedClientFlightId: null,
            cancellationToken, compatibleFingerprints);
        if (replay.IsFailure)
            return replay.Error;
        if (replay.Value is { } prior)
            return await MobileMutations.ReplayAsync(db, prior, cancellationToken);

        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders.AsNoTracking())
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var actionWindow = await MobileActionWindow.EnsureAvailableAsync(
            db, workOrder.FlightId, timeProvider, cancellationToken);
        if (actionWindow.IsFailure)
            return actionWindow.Error;

        var combinedPayload = new WorkOrderEditableCommandPayload(
            workOrder.ActualFlightNumber.Value,
            workOrder.AircraftType?.AircraftTypeId,
            workOrder.AircraftTailNumber,
            workOrder.Actuals?.Ata,
            workOrder.Actuals?.Atd,
            workOrder.Cancellation?.CanceledAtUtc,
            workOrder.Cancellation?.Reason,
            workOrder.Remarks,
            workOrder.ServiceLines
                .Select(line => new WorkOrderServiceLineCommand(
                    line.Service.ServiceId,
                    line.PerformedBy.Select(performer => performer.StaffMember.StaffMemberId).ToList(),
                    line.Window.From,
                    line.Window.To,
                    line.Description,
                    line.IsReturnToRamp,
                    Id: line.Id,
                    Attachments: null))
                .Concat((request.ServiceLines ?? []).Select(line => line with { Id = null, IsReturnToRamp = true }))
                .ToList(),
            workOrder.Tasks
                .Select(task => new WorkOrderTaskCommand(
                    task.Id,
                    task.TaskType,
                    task.Description,
                    task.Window.From,
                    task.Window.To,
                    task.Employees.Select(e => e.Employee.StaffMemberId).ToList(),
                    task.Tools.Select(t => new WorkOrderTaskToolCommand(t.Tool.ToolId, t.Quantity.Value)).ToList(),
                    task.Materials.Select(m => new WorkOrderTaskMaterialCommand(m.Material.MaterialId, m.Quantity.Value)).ToList(),
                    task.GeneralSupports.Select(g => new WorkOrderTaskGeneralSupportCommand(g.GeneralSupport.GeneralSupportId, g.Quantity.Value)).ToList(),
                    Attachments: null,
                    IsReturnToRamp: task.IsReturnToRamp))
                .Concat((request.Tasks ?? []).Select(task => task with { Id = null, IsReturnToRamp = true }))
                .ToList());

        db.MobileMutations.Add(MobileMutation.Record(
            request.ClientMutationId, userId, mutationKind,
            workOrder.Id, workOrder.FlightId, clientFlightId: null, fingerprint, timeProvider.GetUtcNow()));

        var result = await sender.Send(
            new UpdateWorkOrderCommand(workOrder.Id, workOrder.RowVersion, workOrder.Type, combinedPayload, request.ClientMutationId),
            cancellationToken);

        if (result.IsFailure)
            return result.Error;

        return new MobileWriteResultDto(workOrder.Id, workOrder.FlightId, Idempotent: false);
    }
}

// --- Cancel a flight (cancellation work order) --------------------------------------------

public sealed record MobileCancelFlightCommand(
    Guid FlightId,
    DateTimeOffset CanceledAtUtc,
    string Reason,
    string ClientMutationId) : ICommand<MobileWriteResultDto>;

public sealed class MobileCancelFlightCommandValidator : AbstractValidator<MobileCancelFlightCommand>
{
    public MobileCancelFlightCommandValidator()
    {
        RuleFor(x => x.FlightId).NotEmpty();
        RuleFor(x => x.CanceledAtUtc).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.ClientMutationId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(MobileMutations.IsCanonicalClientMutationId)
            .WithMessage("Client mutation id must be a canonical UUID.");
    }
}

public sealed class MobileCancelFlightCommandHandler(
    IOperationsDbContext db,
    ISender sender,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<MobileCancelFlightCommand, MobileWriteResultDto>
{
    public async Task<Result<MobileWriteResultDto>> Handle(MobileCancelFlightCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        const string mutationKind = "cancel-flight";
        var fingerprint = MobileMutations.Fingerprint(new
        {
            request.FlightId,
            request.CanceledAtUtc,
            request.Reason
        });
        var replay = await MobileMutations.FindReplayAsync(
            db, request.ClientMutationId, userId, mutationKind, fingerprint,
            expectedWorkOrderId: null, expectedFlightId: request.FlightId, expectedClientFlightId: null,
            cancellationToken);
        if (replay.IsFailure)
            return replay.Error;
        if (replay.Value is { } prior)
            return await MobileMutations.ReplayAsync(db, prior, cancellationToken);

        var actionWindow = await MobileActionWindow.EnsureAvailableAsync(
            db, request.FlightId, timeProvider, cancellationToken);
        if (actionWindow.IsFailure)
            return actionWindow.Error;

        var payload = new WorkOrderEditableCommandPayload(
            ActualFlightNumber: null,
            AircraftTypeId: null,
            AircraftTailNumber: null,
            ActualArrivalUtc: null,
            ActualDepartureUtc: null,
            CanceledAtUtc: request.CanceledAtUtc,
            CancellationReason: request.Reason,
            Remarks: null,
            ServiceLines: [],
            Tasks: []);

        var workOrderId = Guid.NewGuid();
        db.MobileMutations.Add(MobileMutation.Record(
            request.ClientMutationId, userId, mutationKind,
            workOrderId, request.FlightId, clientFlightId: null, fingerprint, timeProvider.GetUtcNow()));

        var result = await sender.Send(
            new SubmitWorkOrderCommand(request.FlightId, WorkOrderType.Cancellation, payload, request.ClientMutationId, workOrderId),
            cancellationToken);

        if (result.IsFailure)
            return result.Error;

        return new MobileWriteResultDto(result.Value, request.FlightId, Idempotent: false);
    }
}

// --- Invite teammates (online-only, no outbox) ---------------------------------------------

public sealed record MobileInviteEmployeesCommand(
    Guid FlightId,
    IReadOnlyList<Guid> InviteeStaffMemberIds) : ICommand;

public sealed class MobileInviteEmployeesCommandValidator : AbstractValidator<MobileInviteEmployeesCommand>
{
    public MobileInviteEmployeesCommandValidator()
    {
        RuleFor(x => x.FlightId).NotEmpty();
        RuleFor(x => x.InviteeStaffMemberIds).NotEmpty();
    }
}

public sealed class MobileInviteEmployeesCommandHandler(
    IOperationsDbContext db,
    ISender sender,
    TimeProvider timeProvider) : ICommandHandler<MobileInviteEmployeesCommand>
{
    public async Task<Result> Handle(MobileInviteEmployeesCommand request, CancellationToken cancellationToken)
    {
        var actionWindow = await MobileActionWindow.EnsureAvailableAsync(
            db, request.FlightId, timeProvider, cancellationToken);
        if (actionWindow.IsFailure)
            return actionWindow.Error;

        // The mobile client is online for invites but never holds a fresh RowVersion; resolve it
        // server-side. Add-only semantics and scope checks live in the inner command.
        var rowVersion = await db.Flights.AsNoTracking()
            .Where(f => f.Id == request.FlightId)
            .Select(f => f.RowVersion)
            .FirstOrDefaultAsync(cancellationToken);
        if (rowVersion is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        return await sender.Send(
            new InviteEmployeesToFlightCommand(request.FlightId, request.InviteeStaffMemberIds, rowVersion),
            cancellationToken);
    }
}
