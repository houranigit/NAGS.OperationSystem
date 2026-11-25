using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Localization;
using OperationsSystem.Blazor.Client.State;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public partial class PortalFlightWorkDialog
{
    public enum ApprovalAction { None, Approve, Unlock }

    private static readonly string[] WorkStepLabels = ["Arrival", "Services", "Tasks", "Departure"];
    private static readonly string[] CancellationStepLabels = ["Cancellation"];
    private static readonly Guid UnknownCustomer = new("50000000-0000-0000-0000-000000000001");
    private const string ArrivalTimeField = "arrival-time";
    private const string AircraftField = "arrival-aircraft";
    private const string ReceiverField = "arrival-receiver";
    private const string HandlerField = "departure-handler";
    private const string TailField = "arrival-tail";
    private const string ArrivalRemarksField = "arrival-remarks";
    private const string DepartureTimeField = "departure-time";

    [Inject] private OperationsApiClient Operations { get; set; } = default!;
    [Inject] private MasterDataApiClient MasterData { get; set; } = default!;
    [Inject] private DialogService DialogService { get; set; } = default!;
    [Inject] private AuthSession Auth { get; set; } = default!;

    [Parameter, EditorRequired] public Guid FlightId { get; set; }
    [Parameter] public int InitialStep { get; set; }
    [Parameter] public bool ViewOnly { get; set; }
    [Parameter] public bool AdministratorEntry { get; set; }
    [Parameter] public ApprovalAction ReviewAction { get; set; }

    private readonly CancellationTokenSource lifetime = new();
    private PortalFlightOperations? flight;
    private StationTimeZone zone = default!;
    private PortalWorkLookups lookups = default!;
    private PortalFlightArrival? arrivalSource;
    private PortalOperationTime arrivalTime = default!;
    private Guid? aircraftTypeId;
    private Guid? receivedByStaffMemberId;
    private Guid? handledByStaffMemberId;
    private string? tailNumber;
    private string? arrivalReference;
    private string? arrivalRemarks;
    private PortalFlightDeparture? departureSource;
    private PortalOperationTime departureTime = default!;
    private string? departureRemarks;
    private PortalSignatureDraft signature = new();
    private List<PortalWorkDraft> services = [];
    private List<PortalWorkDraft> tasks = [];
    private readonly List<PortalReturnToRampDraft> pendingReturns = [];
    private PortalFlightValidation validation = new();
    private int validationFocusAttempt;
    private bool requireArrivalForValidation;
    private bool savedChanges;
    private string? error;
    private bool loading = true;
    private bool saving;
    private bool deletingOrder;
    private int step;

    private IEnumerable<(Guid Id, int Sequence)> ReturnSteps =>
        (flight?.ReturnToRamps.Select(item => (item.Id, item.Sequence)) ?? [])
        .Concat(pendingReturns.Select(item => (item.Id, item.Sequence)))
        .OrderBy(item => item.Sequence);

    private string[] StepLabels => IsCancellationReview
        ? CancellationStepLabels
        : [.. WorkStepLabels, .. ReturnSteps.Select(item => UiStrings.ReturnsToRamp.Step(item.Sequence))];

    private Guid? CurrentReturnId => step < WorkStepLabels.Length
        ? null
        : ReturnSteps.Skip(step - WorkStepLabels.Length).Select(item => (Guid?)item.Id).FirstOrDefault();
    private bool IsCancellationReview => flight is
    {
        CancellationSubmissions.Count: > 0, ArrivalSubmissions.Count: 0, DepartureSubmissions.Count: 0,
        Services.Count: 0, Tasks.Count: 0, ReturnToRamps.Count: 0
    };

    private bool IsApprovalReview => ReviewAction != ApprovalAction.None;
    private bool WorkReadOnly => ViewOnly || IsApprovalReview;
    private bool CanDeleteOrder => !IsApprovalReview && PortalOrderDeletion.CanDelete(Auth, flight, PortalFlightOrderKind.WorkOrder);
    private bool CanChangeApproval => ReviewAction switch
    {
        ApprovalAction.Approve => flight?.CanApprove == true,
        ApprovalAction.Unlock => flight?.CanUnlock == true,
        _ => false
    };

    private bool NormalWorkReadOnly => WorkReadOnly || IsCancellationReview || flight?.IsApprovalLocked == true;
    private bool RequiresArrivalRemarks => flight?.IsAdHocFlight == true && flight.CustomerId == UnknownCustomer;
    private bool CanEditArrival => !NormalWorkReadOnly && flight is { CanSaveArrival: true } data &&
        (!data.IsUnassignedSearchFlight || arrivalSource is null || IsStaffSubmitter(arrivalSource.SubmittedByEmployee, data.CurrentStaffMemberId));

    private bool CanEditDeparture => !NormalWorkReadOnly && flight is { CanSaveDeparture: true } data &&
        (!data.IsUnassignedSearchFlight || departureSource is null || IsStaffSubmitter(departureSource.SubmittedByEmployee, data.CurrentStaffMemberId));

    private bool CanEditNormalWork => !NormalWorkReadOnly && flight is { } data && (data.CanSaveArrival || data.CanSaveDeparture ||
        data.CanSaveServices || data.CanSaveTasks || services.Concat(tasks).Any(row => row.CanEdit || row.CanSetEnd || row.CanDelete));

    private DateTimeOffset? ActualArrival => arrivalTime.TryReadUtc(out var time, out _) ? time : null;
    private DateTimeOffset? ActualDeparture => departureTime.TryReadUtc(out var time, out _) ? time : null;

    private bool DepartureFieldsAvailable
    {
        get
        {
            var checks = new PortalFlightValidation();
            ReadArrival(checks, requireArrival: true);
            PortalWorkDraft.ValidateDeparturePeriods(services, tasks, checks, DepartureTimeField);
            return checks.Count == 0;
        }
    }

    private bool CanDraftReturnToRamp => !WorkReadOnly &&
        flight is { CancellationSubmissions.Count: 0 } data &&
        (data.CanSaveReturnsToRamp || data.DepartureSubmissions.Count == 0 && CanEditDeparture);

    private bool ShowAddReturnToRamp => !WorkReadOnly && flight is { CancellationSubmissions.Count: 0 } data &&
        (data.CanSaveReturnsToRamp || CanEditNormalWork || data.CanApprove);

    private bool HasOpenReturn => flight?.ReturnToRamps.Any(item => item.IsOpen) == true || pendingReturns.Any(item => item.IsOpen);

    private bool CanAddReturnToRamp => CanDraftReturnToRamp && !HasOpenReturn &&
        (flight is { DepartureSubmissions.Count: > 0 } || HasValidDepartureDraft());

    private bool HasValidDepartureDraft()
    {
        if (!ActualDeparture.HasValue)
            return false;

        var checks = new PortalFlightValidation();
        ReadArrival(checks, requireArrival: true);
        return ReadDeparture(checks) is not null && checks.Count == 0;
    }

    private string? AddReturnToRampHint
    {
        get
        {
            if (flight is not { } data || !ShowAddReturnToRamp)
                return null;

            if (HasOpenReturn)
                return UiStrings.ReturnsToRamp.OpenBlocksAdd;

            if (data.DepartureSubmissions.Count == 0 && CanEditDeparture)
                return UiStrings.ReturnsToRamp.CompleteDepartureBeforeAdd;

            return UiStrings.ReturnsToRamp.AddUnavailable;
        }
    }

    private bool IsGuidedEntry => flight is
    {
        ArrivalSubmissions.Count: 0,
        DepartureSubmissions.Count: 0,
        Services.Count: 0,
        Tasks.Count: 0,
        ReturnToRamps.Count: 0
    } && CanEditNormalWork;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        loading = true;
        error = null;
        try
        {
            var data = await Operations.GetPortalFlightOperationsAsync(FlightId, lifetime.Token);
            zone = StationTimeZone.FromId(data.StationTimeZoneId);
            lookups = await LoadWorkLookupsAsync(data);
            flight = data;
            services = data.Services.Select(item => PortalWorkDraft.From(item, zone)).ToList();
            tasks = data.Tasks.Select(item => PortalWorkDraft.From(item, zone)).ToList();
            AddPlannedServiceDrafts(data);
            SetArrival(data.ArrivalSubmissions.OrderByDescending(item => IsStaffSubmitter(item.SubmittedByEmployee, data.CurrentStaffMemberId))
                .ThenByDescending(item => item.IsAccepted).FirstOrDefault());
            SetDeparture(data.DepartureSubmissions.OrderByDescending(item => IsStaffSubmitter(item.SubmittedByEmployee, data.CurrentStaffMemberId))
                .ThenByDescending(item => item.IsAccepted).FirstOrDefault());
            step = IsGuidedEntry ? 0 : Math.Clamp(InitialStep, 0, StepLabels.Length - 1);
            if (IsApprovalReview && !CanChangeApproval)
                error = "This approval action is no longer available. Reopen the Work Order menu.";
        }
        catch (ApiException ex)
        {
            error = ex.ToDisplayMessage("Could not load the flight work.");
        }
        catch (JSException)
        {
            error = "Could not load the flight work. Try again.";
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            error = "The station time zone is unavailable.";
        }
        finally
        {
            loading = false;
        }
    }

    private Task<PortalWorkLookups> LoadWorkLookupsAsync(PortalFlightOperations data) => WorkReadOnly
        ? Task.FromResult(new PortalWorkLookups([], [], [], [], [], [], [], false))
        : PortalWorkLookups.LoadAsync(MasterData, data, occurrenceOnly: false,
            administratorEntry: AdministratorEntry, ct: lifetime.Token);

    private void AddPlannedServiceDrafts(PortalFlightOperations data)
    {
        if (NormalWorkReadOnly || !(data.CanAddServices ?? data.CanSaveServices) ||
            !AdministratorEntry && !data.CurrentStaffMemberId.HasValue)
            return;

        var staffId = data.CurrentStaffMemberId;
        var staff = lookups.Staff.FirstOrDefault(item => item.Id == staffId);
        foreach (var planned in data.PlannedServices.Where(item => !item.IsAircraftPerLanding
            && lookups.Services.Any(choice => choice.Id == item.ServiceId)
            && data.Services.All(existing => existing.ServiceId != item.ServiceId)))
            services.Add(PortalWorkDraft.FromPlan(planned, staffId, staff, zone));
    }

    private void SetArrival(PortalFlightArrival? source)
    {
        unlockedArrivalId = null;
        arrivalSource = source;
        receivedByStaffMemberId = source?.ReceivedByEmployee.StaffMemberId;
        arrivalTime = new(zone, source?.Ata);
        aircraftTypeId = source?.AircraftTypeId ?? flight?.AircraftTypeId;
        tailNumber = source?.AircraftTailNumber;
        arrivalReference = source?.CustomerWorkReferenceNumber;
        arrivalRemarks = source?.Remarks;
    }

    private void SetDeparture(PortalFlightDeparture? source)
    {
        unlockedDepartureId = null;
        departureSource = source;
        handledByStaffMemberId = source?.HandledByEmployee.StaffMemberId;
        departureTime = new(zone, source?.Atd);
        departureRemarks = source?.Remarks;
        signature = source is { HasSignature: true }
            ? new($"departure-signatures/{source.Id}?expectedUpdatedAt={MediaVersion(source.UpdatedAtUtc ?? source.CreatedAtUtc)}")
            : new();
    }

    internal static string MediaVersion(DateTimeOffset stamp) => Uri.EscapeDataString(stamp.ToString("O"));

    private IEnumerable<PortalCatalogChoice> ArrivalOptions() => flight!.ArrivalSubmissions
        .Select(item => new PortalCatalogChoice(item.Id, $"{item.SubmittedByEmployee?.FullName ?? item.CreatedByDisplayName ?? "Unknown submitter"} · {zone.Format(item.Ata)}"));

    private IEnumerable<PortalCatalogChoice> DepartureOptions() => flight!.DepartureSubmissions
        .Select(item => new PortalCatalogChoice(item.Id, $"{item.SubmittedByEmployee?.FullName ?? item.CreatedByDisplayName ?? "Unknown submitter"} · {zone.Format(item.Atd)}"));

    private async Task SelectArrivalAsync(Guid? id)
    {
        if (id is not { } target || target == arrivalSource?.Id || ArrivalChanged() && !await ConfirmDiscardAsync())
            return;

        SetArrival(flight!.ArrivalSubmissions.Single(item => item.Id == target));
        validation.ClearStep(0);
    }

    private async Task SelectDepartureAsync(Guid? id)
    {
        if (id is not { } target || target == departureSource?.Id || signature.IsReading || DepartureChanged() && !await ConfirmDiscardAsync())
            return;

        SetDeparture(flight!.DepartureSubmissions.Single(item => item.Id == target));
        validation.ClearStep(3);
    }

    private bool ArrivalChanged() => arrivalTime.Local != Local(arrivalSource?.Ata) ||
        aircraftTypeId != (arrivalSource?.AircraftTypeId ?? flight?.AircraftTypeId) ||
        PortalWorkDraft.Trim(tailNumber) != arrivalSource?.AircraftTailNumber ||
        PortalWorkDraft.Trim(arrivalReference) != arrivalSource?.CustomerWorkReferenceNumber ||
        PortalWorkDraft.Trim(arrivalRemarks) != arrivalSource?.Remarks ||
        receivedByStaffMemberId != arrivalSource?.ReceivedByEmployee.StaffMemberId;

    private bool DepartureChanged() => departureTime.Local != Local(departureSource?.Atd) ||
        PortalWorkDraft.Trim(departureRemarks) != departureSource?.Remarks || signature.Bytes is not null ||
        handledByStaffMemberId != departureSource?.HandledByEmployee.StaffMemberId;

    private DateTime? Local(DateTimeOffset? value) => value is { } time ? zone.ToLocalDateTime(time) : null;

    private DateTimeOffset? ArrivalFor(Guid? staffId)
    {
        var report = flight!.ArrivalSubmissions.OrderByDescending(item => IsStaffSubmitter(item.SubmittedByEmployee, staffId))
            .ThenByDescending(item => item.IsAccepted).ThenBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).FirstOrDefault();
        return report?.Id == arrivalSource?.Id ? ActualArrival : report?.Ata;
    }

    private DateTimeOffset? DepartureFor(Guid? staffId)
    {
        var report = flight!.DepartureSubmissions.OrderByDescending(item => IsStaffSubmitter(item.SubmittedByEmployee, staffId))
            .ThenByDescending(item => item.IsAccepted).ThenBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).FirstOrDefault();
        return report?.Id == departureSource?.Id ? ActualDeparture : report?.Atd;
    }

    private void ValidateNormalBounds(IReadOnlyList<PortalWorkDraft> work, PortalFlightValidation errors)
    {
        var staffId = flight!.CurrentStaffMemberId;
        PortalWorkDraft.ValidateActualBounds(work, errors, ArrivalFor(staffId), DepartureFor(staffId));
        if (arrivalTime.HasChanged)
            PortalWorkDraft.ValidateActualBounds(work, errors, ActualArrival,
                DepartureFor(arrivalSource?.SubmittedByEmployee?.StaffMemberId ?? staffId), includeUnchanged: true);
        if (departureTime.HasChanged)
            PortalWorkDraft.ValidateActualBounds(work, errors,
                ArrivalFor(departureSource?.SubmittedByEmployee?.StaffMemberId ?? staffId), ActualDeparture, includeUnchanged: true);
    }

    private bool NormalWorkChanged() => ArrivalChanged() || DepartureChanged() || services.Concat(tasks).Any(row => row.HasChanges());
    private bool WorkChanged() => NormalWorkChanged() || pendingReturns.Count > 0;
    private bool WorkFilesLoading() => signature.IsReading || services.Concat(tasks).Any(row => row.IsReadingFile);

    private void ChangeStep(int index)
    {
        if (saving || index == step || index < 0 || index >= StepLabels.Length)
            return;

        if (WorkFilesLoading())
        {
            error = "Wait for the file to finish loading before changing steps.";
            return;
        }

        if (!NormalWorkReadOnly && index > 0 && index < WorkStepLabels.Length)
        {
            validation.ClearStep(0);
            requireArrivalForValidation = true;
            ReadArrival(validation, requireArrival: true);
            if (validation.HasErrorsInStep(0))
            {
                FocusFirstInvalidStep();
                return;
            }
        }

        step = index;
        error = null;
    }

    private void SelectReturn(Guid id)
    {
        var offset = ReturnSteps.Select(item => item.Id).ToList().IndexOf(id);
        step = offset >= 0 ? WorkStepLabels.Length + offset : Math.Min(step, StepLabels.Length - 1);
    }

    private void NextStep()
    {
        if (!IsGuidedEntry || saving)
            return;
        if (WorkFilesLoading())
        {
            error = "Wait for the file to finish loading before changing steps.";
            return;
        }

        validation.ClearStep(step);
        if (step == 0)
            requireArrivalForValidation = true;
        error = null;
        ValidateCurrentStep();
        if (!validation.HasErrorsInStep(step))
            ChangeStep(step + 1);
        else
            validationFocusAttempt++;
    }

    private void RefreshValidation()
    {
        if (!validation.HasErrorsInStep(step))
            return;

        validation.ClearStep(step);
        ValidateCurrentStep();
    }

    private void ValidateCurrentStep()
    {
        switch (step)
        {
            case 0:
                ReadArrival(validation, requireArrival: requireArrivalForValidation);
                break;
            case 1:
                ReadServices(validation);
                break;
            case 2:
                ReadTasks(validation);
                break;
            case 3:
                ReadDeparture(validation);
                break;
        }
    }

    private PortalFlightArrivalRequest? ReadArrival(PortalFlightValidation errors, bool requireArrival = false)
    {
        if (!ArrivalChanged() && (!requireArrival || arrivalSource is not null))
            return null;

        errors.Step = 0;
        if (!CanEditArrival)
        {
            if (requireArrival && arrivalSource is null)
                errors.Add(ArrivalTimeField, "Arrival details are required before continuing.");
            return null;
        }

        var ata = arrivalTime.Read("Actual time of arrival", errors, ArrivalTimeField, required: true);
        if (aircraftTypeId is null || aircraftTypeId == Guid.Empty)
            errors.Add(AircraftField, "Aircraft type is required.");
        if (string.IsNullOrWhiteSpace(tailNumber))
            errors.Add(TailField, "Tail number is required.");
        if (AdministratorEntry && (receivedByStaffMemberId is null || receivedByStaffMemberId == Guid.Empty))
            errors.Add(ReceiverField, "Receiving employee is required.");
        if (RequiresArrivalRemarks && string.IsNullOrWhiteSpace(arrivalRemarks))
            errors.Add(ArrivalRemarksField, "Describe the Unknown customer in Arrival remarks.");

        return new(ata.GetValueOrDefault(), aircraftTypeId.GetValueOrDefault(), tailNumber?.Trim() ?? string.Empty,
            PortalWorkDraft.Trim(arrivalReference), PortalWorkDraft.Trim(arrivalRemarks), false, arrivalSource?.Id,
            AdministratorEntry ? receivedByStaffMemberId : null);
    }

    private PortalFlightDepartureRequest? ReadDeparture(PortalFlightValidation errors)
    {
        if (!DepartureChanged() || !CanEditDeparture)
            return null;

        errors.Step = 3;
        var atd = departureTime.Read("Actual time of departure", errors, DepartureTimeField, required: true);
        if (ArrivalFor(departureSource?.SubmittedByEmployee?.StaffMemberId ?? flight!.CurrentStaffMemberId) is { } ata &&
            atd is { } end && end < ata)
            errors.Add(DepartureTimeField, "Actual departure cannot be before actual arrival.");
        if (atd is { } proposed && FirstReturnArrival() is { } firstArrival &&
            proposed >= firstArrival && proposed != departureSource?.Atd)
            errors.Add(DepartureTimeField, UiStrings.ReturnsToRamp.FlightDepartureBeforeFirstArrival);
        if (AdministratorEntry && (handledByStaffMemberId is null || handledByStaffMemberId == Guid.Empty))
            errors.Add(HandlerField, "Handling employee is required.");
        PortalWorkDraft.ValidateDeparturePeriods(services, tasks, errors, DepartureTimeField);
        var signedAt = signature.Bytes is null ? null : atd;
        return new(atd.GetValueOrDefault(), signature.Bytes, signedAt,
            PortalWorkDraft.Trim(departureRemarks), false, departureSource?.Id,
            AdministratorEntry ? handledByStaffMemberId : null);
    }

    private PortalServiceChange[] ReadServices(PortalFlightValidation errors)
    {
        errors.Step = 1;
        PortalWorkDraft.ValidateServicePeriods(services, errors);
        ValidateNormalBounds(services, errors);
        var closing = DepartureFor(flight!.CurrentStaffMemberId).HasValue;
        return services.Select((row, index) => (row, index)).Where(pair => pair.row.HasChanges())
            .Select(pair => pair.row.ToServiceChange(pair.index + 1, errors, closing)).ToArray();
    }

    private PortalTaskChange[] ReadTasks(PortalFlightValidation errors)
    {
        errors.Step = 2;
        ValidateNormalBounds(tasks, errors);
        var closing = DepartureFor(flight!.CurrentStaffMemberId).HasValue;
        return tasks.Select((row, index) => (row, index)).Where(pair => pair.row.HasChanges())
            .Select(pair => pair.row.ToTaskChange(pair.index + 1, errors, closing)).ToArray();
    }

    private PortalWorkBatchRequest? ReadNormalWork()
    {
        if (NormalWorkReadOnly)
            return null;

        var needsArrival = requireArrivalForValidation || DepartureChanged() ||
            services.Concat(tasks).Any(row => !row.Deleted && row.HasChanges());
        var arrival = ReadArrival(validation, needsArrival);
        var servicesToSave = ReadServices(validation);
        var tasksToSave = ReadTasks(validation);
        var departure = ReadDeparture(validation);
        return arrival is null && departure is null && servicesToSave.Length == 0 && tasksToSave.Length == 0
            ? null
            : new(arrival, departure, servicesToSave, tasksToSave);
    }

    private async Task SaveAsync()
    {
        if (flight is null || saving || WorkReadOnly)
            return;

        error = null;
        if (WorkFilesLoading())
        {
            error = "Wait for the selected file to finish loading.";
            return;
        }

        if (!WorkChanged())
        {
            if (validation.Count > 0)
                FocusFirstInvalidStep();
            else
                DialogService.Close(savedChanges);
            return;
        }

        requireArrivalForValidation = requireArrivalForValidation && validation.HasErrorsInStep(0);
        validation = new();
        var request = ReadNormalWork();
        if (pendingReturns.Count > 0 && flight.DepartureSubmissions.Count == 0 && request?.Departure is null)
        {
            validation.Step = 3;
            validation.Add(DepartureTimeField, UiStrings.ReturnsToRamp.CompleteDepartureBeforeSave);
        }

        if (validation.Count > 0)
        {
            FocusFirstInvalidStep();
            return;
        }

        saving = true;
        int? savingReturn = null;
        var selectedReturnId = CurrentReturnId;
        try
        {
            if (request is not null)
            {
                if ((request.Arrival is not null || request.Departure is not null) && flight.CancellationSubmissions.Count > 0)
                {
                    if (await DialogService.Confirm("This flight has a cancellation report. Save the operational report?",
                        "Conflicting flight report", new ConfirmOptions { OkButtonText = "Save report", CancelButtonText = "Go back" }) != true)
                        return;

                    request = request with
                    {
                        Arrival = request.Arrival is { } arrival ? arrival with { AcknowledgeConflictingSubmission = true } : null,
                        Departure = request.Departure is { } departure ? departure with { AcknowledgeConflictingSubmission = true } : null
                    };
                }
                var saved = await Operations.SavePortalFlightWorkAsync(FlightId, request, flight.RowVersion, lifetime.Token);
                AcceptNormalSave(saved);
            }

            while (pendingReturns.Count > 0)
            {
                var pending = pendingReturns[0];
                savingReturn = pending.Sequence;
                flight = await Operations.SavePortalReturnToRampAsync(FlightId, pending.Id, pending.Request, flight.RowVersion, lifetime.Token);
                savedChanges = true;
                pendingReturns.RemoveAt(0);
                if (selectedReturnId is { } selectedId)
                    SelectReturn(selectedId);
            }
            DialogService.Close(savedChanges);
        }
        catch (ApiException ex)
        {
            error = savingReturn is { } sequence
                ? $"RTR {sequence} still needs to be saved. {ex.ToDisplayMessage("Could not save the return to ramp.")}"
                : ex.ToDisplayMessage("Could not save the flight work.");
        }
        catch (JSException)
        {
            error = "The save could not be confirmed. Try again to check the result.";
        }
        finally
        {
            saving = false;
        }
    }

    private async Task DeleteOrderAsync()
    {
        if (saving || !CanDeleteOrder || flight is not { } current)
            return;

        if (WorkFilesLoading())
        {
            error = "Wait for the file to finish loading before deleting the work order.";
            return;
        }

        saving = true;
        deletingOrder = true;
        try
        {
            if (!await PortalOrderDeletion.ConfirmAsync(DialogService, PortalFlightOrderKind.WorkOrder, current.FlightNumber))
                return;

            error = null;
            await Operations.DeletePortalOrderAsync(FlightId, PortalFlightOrderKind.WorkOrder, current.RowVersion, lifetime.Token);
            DialogService.Close(true);
        }
        catch (ApiException ex)
        {
            error = ex.ToDisplayMessage("Could not delete the work order.");
        }
        catch (JSException)
        {
            error = "The deletion could not be confirmed. Reopen the flight to check its orders.";
        }
        finally
        {
            saving = false;
            deletingOrder = false;
        }
    }

    private async Task ChangeApprovalAsync()
    {
        if (flight is not { } current || saving || !CanChangeApproval)
            return;

        var unlock = ReviewAction == ApprovalAction.Unlock;
        saving = true;
        error = null;
        try
        {
            if (unlock)
                await Operations.UnlockPortalFlightAsync(FlightId, current.RowVersion, lifetime.Token);
            else
                await Operations.ApprovePortalFlightAsync(FlightId, current.RowVersion, lifetime.Token);
            DialogService.Close(true);
        }
        catch (ApiException ex)
        {
            error = ex.ToDisplayMessage(unlock ? "Could not unlock the flight." : "Could not approve the flight.");
        }
        catch (JSException)
        {
            error = "The approval change could not be confirmed. Reopen the flight to check its status.";
        }
        finally
        {
            saving = false;
        }
    }

    private void AcceptNormalSave(PortalFlightOperations saved)
    {
        savedChanges = true;
        flight = saved;
        var unrecordedPlan = services.Where(row => row.IsPlanned && !row.HasChanges()).ToArray();
        services = saved.Services.Select(item => PortalWorkDraft.From(item, zone)).Concat(unrecordedPlan).ToList();
        tasks = saved.Tasks.Select(item => PortalWorkDraft.From(item, zone)).ToList();
        SetArrival(saved.ArrivalSubmissions.OrderByDescending(item => item.Id == arrivalSource?.Id)
            .ThenByDescending(item => IsStaffSubmitter(item.SubmittedByEmployee, saved.CurrentStaffMemberId))
            .ThenByDescending(item => item.IsAccepted).FirstOrDefault());
        SetDeparture(saved.DepartureSubmissions.OrderByDescending(item => item.Id == departureSource?.Id)
            .ThenByDescending(item => IsStaffSubmitter(item.SubmittedByEmployee, saved.CurrentStaffMemberId))
            .ThenByDescending(item => item.IsAccepted).FirstOrDefault());
    }

    private void FocusFirstInvalidStep()
    {
        step = validation.FirstInvalidStep;
        validationFocusAttempt++;
    }

    private async Task OpenReturnToRampAsync(Guid? occurrenceId)
    {
        if (flight is null || saving)
            return;

        if (WorkFilesLoading())
        {
            error = "Wait for the file to finish loading before opening a return to ramp.";
            return;
        }

        if (occurrenceId is null || pendingReturns.Any(item => item.Id == occurrenceId))
        {
            await OpenReturnToRampDraftAsync(occurrenceId);
            return;
        }

        var occurrence = flight.ReturnToRamps.SingleOrDefault(item => item.Id == occurrenceId);
        if (occurrence is null)
            return;

        var result = await DialogService.OpenAsync<PortalReturnToRampDialog>($"RTR {occurrence.Sequence} · {flight.FlightNumber}",
            new Dictionary<string, object?>
            {
                [nameof(PortalReturnToRampDialog.FlightId)] = FlightId,
                [nameof(PortalReturnToRampDialog.OccurrenceId)] = occurrenceId,
                [nameof(PortalReturnToRampDialog.ViewOnly)] = WorkReadOnly || !flight.CanEditReturnToRamp(occurrence) || WorkChanged(),
                [nameof(PortalReturnToRampDialog.AdministratorEntry)] = AdministratorEntry
            },
            PortalWorkDialogLayout.CreateOptions());
        if (result is PortalFlightOperations updated)
        {
            AcceptNormalSave(updated);
            SelectReturn(occurrence.Id);
            error = null;
        }
    }

    private async Task OpenReturnToRampDraftAsync(Guid? id)
    {
        if (flight is null || !CanDraftReturnToRamp || id is null && !CanAddReturnToRamp)
            return;

        var index = pendingReturns.FindIndex(item => item.Id == id);
        var draft = index >= 0 ? pendingReturns[index] : null;
        var sequence = draft?.Sequence ?? flight.LastReturnToRampSequence + pendingReturns.Count + 1;
        var localDeparture = CanEditDeparture && DepartureChanged() ? ActualDeparture : null;
        var flightDeparture = PortalReturnToRampTimeline.LatestFlightDeparture(flight,
            localDeparture.HasValue ? departureSource?.Id : null, localDeparture);
        var timeline = PortalReturnToRampTimeline.ForOccurrence(flight, sequence, flightDeparture, pendingReturns);
        var result = await DialogService.OpenAsync<PortalReturnToRampDialog>(draft is null
            ? $"Add RTR {sequence} · {flight.FlightNumber}" : $"Edit RTR {sequence} · {flight.FlightNumber}",
            new Dictionary<string, object?>
            {
                [nameof(PortalReturnToRampDialog.FlightId)] = FlightId,
                [nameof(PortalReturnToRampDialog.AdministratorEntry)] = AdministratorEntry,
                [nameof(PortalReturnToRampDialog.DraftFlight)] = flight,
                [nameof(PortalReturnToRampDialog.Draft)] = draft,
                [nameof(PortalReturnToRampDialog.DraftSequence)] = sequence,
                [nameof(PortalReturnToRampDialog.DraftTimeline)] = timeline
            },
            PortalWorkDialogLayout.CreateOptions());
        if (result is not PortalReturnToRampDraft pending)
            return;

        if (index >= 0)
            pendingReturns[index] = pending;
        else
            pendingReturns.Add(pending);
        SelectReturn(pending.Id);
        error = null;
    }

    private DateTimeOffset? FirstReturnArrival() =>
        flight?.ReturnToRamps.MinBy(item => item.Sequence)?.ArrivedAtUtc ??
        pendingReturns.FirstOrDefault()?.Request.Arrival.ArrivedAtUtc;

    private void RemoveReturnToRampDraft(Guid id)
    {
        pendingReturns.RemoveAll(item => item.Id == id);
        for (var index = 0; index < pendingReturns.Count; index++)
            pendingReturns[index] = pendingReturns[index] with { Sequence = flight!.LastReturnToRampSequence + index + 1 };
        step = Math.Min(step, StepLabels.Length - 1);
    }

    private void CloseWithoutSaving() => DialogService.Close(false);

    private async Task CloseAsync()
    {
        if (WorkFilesLoading())
        {
            error = "Wait for the file to finish loading before closing.";
            return;
        }

        if (!WorkChanged() || await ConfirmDiscardAsync())
            DialogService.Close(savedChanges);
    }

    private async Task<bool> ConfirmDiscardAsync() => await DialogService.Confirm(
        "Discard the unsaved changes?", "Discard changes", new ConfirmOptions { OkButtonText = "Discard", CancelButtonText = "Keep editing" }) == true;

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        lifetime.Dispose();
    }
}
