using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.State;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public partial class PortalFlightWorkDialog
{
    private static readonly string[] StepLabels = ["Arrival", "Services", "Tasks", "Departure", "Returns to ramp"];
    private static readonly Guid UnknownCustomer = new("50000000-0000-0000-0000-000000000001");
    private const string ArrivalTimeField = "arrival-time";
    private const string AircraftField = "arrival-aircraft";
    private const string ReceiverField = "arrival-receiver";
    private const string HandlerField = "departure-handler";
    private const string TailField = "arrival-tail";
    private const string ArrivalRemarksField = "arrival-remarks";
    private const string DepartureTimeField = "departure-time";
    private const string SignatureTimeField = "departure-signature-time";

    [Inject] private OperationsApiClient Operations { get; set; } = default!;
    [Inject] private MasterDataApiClient MasterData { get; set; } = default!;
    [Inject] private DialogService DialogService { get; set; } = default!;

    [Parameter, EditorRequired] public Guid FlightId { get; set; }
    [Parameter] public int InitialStep { get; set; }
    [Parameter] public bool ViewOnly { get; set; }
    [Parameter] public bool AdministratorEntry { get; set; }

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
    private bool requireDepartureForValidation;
    private bool savedChanges;
    private string? error;
    private bool loading = true;
    private bool saving;
    private int step;

    private bool RequiresArrivalRemarks => flight?.IsAdHocFlight == true && flight.CustomerId == UnknownCustomer;
    private bool CanEditArrival => !ViewOnly && flight is { CanSaveArrival: true } data &&
        (!data.IsUnassignedSearchFlight || arrivalSource is null || IsStaffSubmitter(arrivalSource.SubmittedByEmployee, data.CurrentStaffMemberId));

    private bool CanEditDeparture => !ViewOnly && flight is { CanSaveDeparture: true } data &&
        (!data.IsUnassignedSearchFlight || departureSource is null || IsStaffSubmitter(departureSource.SubmittedByEmployee, data.CurrentStaffMemberId));

    private bool CanEditNormalWork => !ViewOnly && flight is { } data && (data.CanSaveArrival || data.CanSaveDeparture ||
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

    private bool CanDraftReturnToRamp => !ViewOnly && flight is { CancellationSubmissions.Count: 0 } data &&
        (data.CanSaveReturnsToRamp || data.CanSaveDeparture && data.DepartureSubmissions.Count == 0);

    private bool CanAddReturnToRamp => CanDraftReturnToRamp && ActualDeparture.HasValue &&
        flight!.ReturnToRamps.All(item => !item.IsOpen) && pendingReturns.All(item => !item.IsOpen);

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
            lookups = ViewOnly
                ? new([], [], [], [], [], [], [], false)
                : await PortalWorkLookups.LoadAsync(MasterData, data, occurrenceOnly: false,
                    administratorEntry: AdministratorEntry, ct: lifetime.Token);
            services = data.Services.Select(item => PortalWorkDraft.From(item, zone)).ToList();
            tasks = data.Tasks.Select(item => PortalWorkDraft.From(item, zone)).ToList();
            if (!ViewOnly && (data.CanAddServices ?? data.CanSaveServices) && (AdministratorEntry || data.CurrentStaffMemberId.HasValue))
            {
                var staffId = data.CurrentStaffMemberId;
                var staff = lookups.Staff.FirstOrDefault(item => item.Id == staffId);
                foreach (var planned in data.PlannedServices.Where(item => !item.IsAircraftPerLanding
                    && lookups.Services.Any(choice => choice.Id == item.ServiceId)
                    && data.Services.All(existing => existing.ServiceId != item.ServiceId)))
                    services.Add(PortalWorkDraft.FromPlan(planned, staffId, staff, zone));
            }

            flight = data;
            SetArrival(data.ArrivalSubmissions.OrderByDescending(item => IsStaffSubmitter(item.SubmittedByEmployee, data.CurrentStaffMemberId))
                .ThenByDescending(item => item.IsAccepted).FirstOrDefault());
            SetDeparture(data.DepartureSubmissions.OrderByDescending(item => IsStaffSubmitter(item.SubmittedByEmployee, data.CurrentStaffMemberId))
                .ThenByDescending(item => item.IsAccepted).FirstOrDefault());
            step = IsGuidedEntry ? 0 : Math.Clamp(InitialStep, 0, StepLabels.Length - 1);
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
        if (saving || index == step)
            return;

        if (WorkFilesLoading())
        {
            error = "Wait for the file to finish loading before changing steps.";
            return;
        }

        if (!ViewOnly && index == 4)
        {
            if (!ValidateReturnToRampEntry())
                return;
        }
        else if (!ViewOnly && index > 0)
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
                ReadDeparture(validation, requireDeparture: requireDepartureForValidation);
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

    private PortalFlightDepartureRequest? ReadDeparture(PortalFlightValidation errors, bool requireDeparture = false)
    {
        if (!DepartureChanged() && (!requireDeparture || departureSource is not null))
            return null;

        errors.Step = 3;
        if (!CanEditDeparture)
        {
            if (requireDeparture && departureSource is null)
                errors.Add(DepartureTimeField, "Departure details are required before adding a return to ramp.");
            return null;
        }

        var atd = departureTime.Read("Actual time of departure", errors, DepartureTimeField, required: true);
        if (ArrivalFor(departureSource?.SubmittedByEmployee?.StaffMemberId ?? flight!.CurrentStaffMemberId) is { } ata &&
            atd is { } end && end < ata)
            errors.Add(DepartureTimeField, "Actual departure cannot be before actual arrival.");
        if (AdministratorEntry && (handledByStaffMemberId is null || handledByStaffMemberId == Guid.Empty))
            errors.Add(HandlerField, "Handling employee is required.");
        PortalWorkDraft.ValidateDeparturePeriods(services, tasks, errors, DepartureTimeField);
        var signedAt = signature.SigningTime?.Read("Signed at", errors, SignatureTimeField, required: true);
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

    private async Task SaveAsync()
    {
        if (flight is null || saving || ViewOnly)
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
        var needsArrival = requireArrivalForValidation || DepartureChanged() || pendingReturns.Count > 0 ||
            services.Concat(tasks).Any(row => !row.Deleted && row.HasChanges());
        var arrival = ReadArrival(validation, needsArrival);
        var serviceChanges = ReadServices(validation);
        var taskChanges = ReadTasks(validation);
        var departure = ReadDeparture(validation, requireDeparture: pendingReturns.Count > 0);
        if (validation.Count > 0)
        {
            FocusFirstInvalidStep();
            return;
        }

        saving = true;
        int? savingReturn = null;
        try
        {
            var acknowledged = false;
            if ((arrival is not null || departure is not null) && flight.CancellationSubmissions.Count > 0)
            {
                acknowledged = await DialogService.Confirm(
                    "This flight has a cancellation report. Save the operational report?",
                    "Conflicting flight report", new ConfirmOptions { OkButtonText = "Save report", CancelButtonText = "Go back" }) == true;
                if (!acknowledged)
                    return;
            }

            var request = new PortalWorkBatchRequest(
                arrival is null ? null : arrival with { AcknowledgeConflictingSubmission = acknowledged },
                departure is null ? null : departure with { AcknowledgeConflictingSubmission = acknowledged },
                serviceChanges, taskChanges);
            if (arrival is not null || departure is not null || serviceChanges.Length > 0 || taskChanges.Length > 0)
            {
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

    private bool ValidateReturnToRampEntry()
    {
        validation = new();
        requireArrivalForValidation = true;
        requireDepartureForValidation = true;
        ReadArrival(validation, requireArrival: true);
        ReadServices(validation);
        ReadTasks(validation);
        ReadDeparture(validation, requireDeparture: true);
        if (validation.Count == 0)
            return true;

        FocusFirstInvalidStep();
        return false;
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
                [nameof(PortalReturnToRampDialog.ViewOnly)] = ViewOnly || WorkChanged(),
                [nameof(PortalReturnToRampDialog.AdministratorEntry)] = AdministratorEntry
            },
            PortalWorkDialogLayout.CreateOptions());
        if (result is true)
            DialogService.Close(true);
    }

    private async Task OpenReturnToRampDraftAsync(Guid? id)
    {
        if (flight is null || !CanDraftReturnToRamp || id is null && !CanAddReturnToRamp)
            return;

        if (!ValidateReturnToRampEntry())
            return;

        var index = pendingReturns.FindIndex(item => item.Id == id);
        var draft = index >= 0 ? pendingReturns[index] : null;
        var sequence = draft?.Sequence ?? flight.LastReturnToRampSequence + pendingReturns.Count + 1;
        var result = await DialogService.OpenAsync<PortalReturnToRampDialog>(draft is null
            ? $"Add RTR {sequence} · {flight.FlightNumber}" : $"Edit RTR {sequence} · {flight.FlightNumber}",
            new Dictionary<string, object?>
            {
                [nameof(PortalReturnToRampDialog.FlightId)] = FlightId,
                [nameof(PortalReturnToRampDialog.AdministratorEntry)] = AdministratorEntry,
                [nameof(PortalReturnToRampDialog.DraftFlight)] = flight,
                [nameof(PortalReturnToRampDialog.Draft)] = draft,
                [nameof(PortalReturnToRampDialog.DraftSequence)] = sequence,
                [nameof(PortalReturnToRampDialog.RequireClosedDraft)] = index >= 0 && index < pendingReturns.Count - 1
            },
            PortalWorkDialogLayout.CreateOptions());
        if (result is not PortalReturnToRampDraft pending)
            return;

        if (index >= 0)
            pendingReturns[index] = pending;
        else
            pendingReturns.Add(pending);
    }

    private void RemoveReturnToRampDraft(Guid id)
    {
        pendingReturns.RemoveAll(item => item.Id == id);
        for (var index = 0; index < pendingReturns.Count; index++)
            pendingReturns[index] = pendingReturns[index] with { Sequence = flight!.LastReturnToRampSequence + index + 1 };
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
