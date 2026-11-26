using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flights;

public sealed record SaveFlightCancellationCommand(Guid FlightId, SaveFlightCancellationRequest Cancellation, byte[] RowVersion,
    bool PortalOnly = false, Guid? TargetSubmissionId = null)
    : ICommand<FlightCancellationSavedDto>;

public sealed class SaveFlightCancellationCommandValidator : AbstractValidator<SaveFlightCancellationCommand>
{
    public SaveFlightCancellationCommandValidator()
    {
        RuleFor(command => command.FlightId).NotEmpty();
        RuleFor(command => command.RowVersion).Must(version => version is { Length: 8 });
        RuleFor(command => command.Cancellation).NotNull();
        When(command => command.Cancellation is not null, () => RuleFor(command => command.Cancellation.CanceledAtUtc).NotEmpty());
    }
}

public sealed class SaveFlightCancellationCommandHandler(
    IOperationsDbContext db,
    IMobileFlightScope scope,
    MasterDataResolver resolver,
    TimeProvider clock,
    IMobileSyncBroadcaster mobileSync,
    IFlightWorkOrderEmailQueue workOrderEmails,
    PortalFlightScope? portalScope = null, IUserDisplayNameReader? userNames = null) : ICommandHandler<SaveFlightCancellationCommand, FlightCancellationSavedDto>
{
    public async Task<Result<FlightCancellationSavedDto>> Handle(SaveFlightCancellationCommand request, CancellationToken cancellationToken)
    {
        var resolved = await FlightOperationAccess.ResolveCorrectionAsync(scope, portalScope, request.PortalOnly, cancellationToken);
        if (resolved.IsFailure) return resolved.Error;
        var access = resolved.Value;
        var administrator = access.IsAdministratorView;
        if (request.TargetSubmissionId.HasValue && !administrator) return PortalFlightScope.Denied();
        var actor = access.StaffActor;
        var flight = await FlightServiceWrites.Load(access.Apply(db.Flights))
            .SingleOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null) return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        FlightCancellationSubmission? existing;
        if (request.TargetSubmissionId is { } targetId)
        {
            existing = flight.CancellationSubmissions.SingleOrDefault(c => c.Id == targetId);
            if (existing is null)
                return Error.NotFound("Cancellation submission not found.", "Operations.CancellationSubmission.NotFound");
        }
        else if (administrator)
        {
            if (flight.CancellationSubmissions.Count > 1)
                return Error.Conflict("Select the cancellation submission to update.", "Operations.CancellationSubmission.TargetRequired");
            existing = flight.CancellationSubmissions.SingleOrDefault();
        }
        else
        {
            existing = flight.CancellationSubmissions.SingleOrDefault(c => c.IsOwnedBy(actor!.StaffMemberId, access.UserId));
        }

        if ((actor is not null || existing is null) && flight.HasSubmittedActuals(actor?.StaffMemberId, access.UserId))
            return Error.Conflict("You already submitted work details for this flight and cannot submit cancellation.",
                "Operations.Flight.OwnWorkSubmitted");
        if (actor is not null && existing is null && FlightSearchPolicy.IsUnassignedRegular(flight, actor.StaffMemberId))
            return Error.Forbidden("Cancellation is read-only on this unassigned flight.", "Operations.Search.CancellationReadOnly");
        var input = request.Cancellation;
        var type = input.CancellationType ?? existing?.Details.Type ?? CancellationType.Unplanned;
        var details = CancellationDetails.Create(input.CanceledAtUtc, input.Remarks, type);
        if (details.IsFailure) return details.Error;
        var names = existing is { SubmittedByEmployee: null } || administrator && existing is null
            ? await FlightWorkAttribution.NamesAsync(userNames, [existing?.CreatedByUserId ?? access.UserId], cancellationToken)
            : null;
        FlightCancellationSavedDto Response(FlightCancellationSubmission submission) =>
            new(FlightCancellationSubmissionDto.Map(submission, names, legacyStaffAttribution: !request.PortalOnly),
                Convert.ToBase64String(flight.RowVersion));

        // A lost successful save can be acknowledged without repeating a write or a warning.
        if (existing is not null && existing.Details == details.Value)
            return Response(existing);
        if (!flight.RowVersion.SequenceEqual(request.RowVersion)) return ConcurrencyErrors.Stale;
        var approvalLock = flight.EnsureApprovalAllowsEdit();
        if (approvalLock.IsFailure) return approvalLock.Error;
        if ((flight.ArrivalSubmissions.Count > 0 || flight.DepartureSubmissions.Count > 0) &&
            !input.AcknowledgeConflictingSubmission)
            return Error.Conflict("Another employee reported arrival or departure for this flight. Confirm before saving cancellation.",
                "Operations.Flight.WorkConfirmationRequired");
        StaffMemberSnapshot? submitter = null;
        if (actor is not null)
        {
            var staff = await resolver.StaffMembersForStationAsync([actor.StaffMemberId], flight.Station.StationId, cancellationToken);
            if (staff.IsFailure) return staff.Error;
            submitter = staff.Value.Single();
        }
        db.SetOriginalRowVersion(flight, request.RowVersion);
        var submitted = flight.SubmitCancellation(submitter, details.Value, access.UserId, clock.GetUtcNow(),
            targetSubmissionId: administrator ? existing?.Id : null, allowUnlinkedSubmitter: administrator);
        if (submitted.IsFailure) return submitted.Error;
        if (actor is not null)
            await FlightActivityRecorder.TouchAsync(db, flight, actor, clock.GetUtcNow(), cancellationToken, recordSearch: !request.PortalOnly);
        var receipt = await workOrderEmails.EnqueueAsync(flight, access.UserId, cancellationToken);
        if (receipt.IsFailure) return receipt.Error;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return ConcurrencyErrors.Stale; }
        catch (DbUpdateException) when (existing is null)
        {
            if (await db.Flights.AsNoTracking().AnyAsync(f => f.Id == request.FlightId && f.CancellationSubmissions.Any(), cancellationToken))
                return ConcurrencyErrors.Stale;
            throw;
        }
        mobileSync.Enqueue(new MobileSyncChange(MobileSyncTables.Flights, MobileSyncOps.Refresh, null,
            MobileSyncAudience.AllStations, clock.GetUtcNow()));
        return Response(submitted.Value);
    }
}

public sealed record RevertFlightCancellationCommand(Guid FlightId, Guid SubmissionId, byte[] RowVersion, bool PortalOnly = false)
    : ICommand<FlightCancellationRevertedDto>;

public sealed class RevertFlightCancellationCommandValidator : AbstractValidator<RevertFlightCancellationCommand>
{
    public RevertFlightCancellationCommandValidator()
    {
        RuleFor(command => command.FlightId).NotEmpty();
        RuleFor(command => command.SubmissionId).NotEmpty();
        RuleFor(command => command.RowVersion).Must(version => version is { Length: 8 });
    }
}

public sealed class RevertFlightCancellationCommandHandler(
    IOperationsDbContext db,
    IMobileFlightScope scope,
    TimeProvider clock,
    IMobileSyncBroadcaster mobileSync,
    IFlightWorkOrderEmailQueue workOrderEmails,
    PortalFlightScope? portalScope = null, IUserDisplayNameReader? userNames = null) : ICommandHandler<RevertFlightCancellationCommand, FlightCancellationRevertedDto>
{
    public async Task<Result<FlightCancellationRevertedDto>> Handle(RevertFlightCancellationCommand request, CancellationToken cancellationToken)
    {
        var resolved = await FlightOperationAccess.ResolveCorrectionAsync(scope, portalScope, request.PortalOnly, cancellationToken);
        if (resolved.IsFailure) return resolved.Error;
        var access = resolved.Value;
        var actor = access.StaffActor;
        var flight = await FlightServiceWrites.Load(access.Apply(db.Flights))
            .SingleOrDefaultAsync(f => f.Id == request.FlightId, cancellationToken);
        if (flight is null) return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");
        var own = flight.CancellationSubmissions.SingleOrDefault(c => c.IsOwnedBy(actor?.StaffMemberId, access.UserId));
        if (own?.Id == request.SubmissionId && !flight.RowVersion.SequenceEqual(request.RowVersion))
            return ConcurrencyErrors.Stale;
        // The submission identity prevents a delayed revert from removing a newer replacement.
        var reverted = flight.RevertCancellation(actor?.StaffMemberId, request.SubmissionId, clock.GetUtcNow(), userId: access.UserId);
        if (reverted.IsFailure) return reverted.Error;
        var names = await FlightWorkAttribution.NamesAsync(userNames, FlightWorkAttribution.UnlinkedCancellationUserIds(flight), cancellationToken);
        if (own is not null)
        {
            db.SetOriginalRowVersion(flight, request.RowVersion);
            if (actor is not null)
                await FlightActivityRecorder.TouchAsync(db, flight, actor, clock.GetUtcNow(), cancellationToken, recordSearch: !request.PortalOnly);
            var receipt = await workOrderEmails.EnqueueAsync(flight, access.UserId, cancellationToken);
            if (receipt.IsFailure) return receipt.Error;
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return ConcurrencyErrors.Stale; }
            mobileSync.Enqueue(new MobileSyncChange(MobileSyncTables.Flights, MobileSyncOps.Refresh, null,
                MobileSyncAudience.AllStations, clock.GetUtcNow()));
        }
        return new FlightCancellationRevertedDto(flight.CancellationSubmissions.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
            .Select(c => FlightCancellationSubmissionDto.Map(c, names, legacyStaffAttribution: !request.PortalOnly)).ToList(), Convert.ToBase64String(flight.RowVersion));
    }
}
