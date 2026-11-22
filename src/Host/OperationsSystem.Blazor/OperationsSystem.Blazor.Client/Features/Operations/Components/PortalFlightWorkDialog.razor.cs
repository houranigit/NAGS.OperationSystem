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

    private readonly CancellationTokenSource lifetime = new();
    private PortalFlightOperations? flight;
    private StationTimeZone zone = default!;
    private PortalWorkLookups lookups = default!;
    private PortalFlightArrival? arrivalSource;
    private PortalOperationTime arrivalTime = default!;
    private Guid? aircraftTypeId;
    private string? tailNumber;
    private string? arrivalReference;
    private string? arrivalRemarks;
    private PortalFlightDeparture? departureSource;
    private PortalOperationTime departureTime = default!;
    private string? departureRemarks;
    private PortalSignatureDraft signature = new();
    private List<PortalWorkDraft> services = [];
    private List<PortalWorkDraft> tasks = [];
    private PortalFlightValidation validation = new();
    private ElementReference validationContainer;
    private int validationFocusAttempt;
    private bool requireArrivalForValidation;
    private string? error;
    private bool loading = true;
    private bool saving;
    private int step;

    private bool RequiresArrivalRemarks => flight?.IsAdHocFlight == true && flight.CustomerId == UnknownCustomer;
    private bool CanEditArrival => !ViewOnly && flight is { CanSaveArrival: true } data &&
        (!data.IsUnassignedSearchFlight || arrivalSource is null || arrivalSource.SubmittedByEmployee.StaffMemberId == data.CurrentStaffMemberId);

    private bool CanEditDeparture => !ViewOnly && flight is { CanSaveDeparture: true } data &&
        (!data.IsUnassignedSearchFlight || departureSource is null || departureSource.SubmittedByEmployee.StaffMemberId == data.CurrentStaffMemberId);

    private bool CanEditNormalWork => !ViewOnly && flight is { } data && (data.CanSaveArrival || data.CanSaveDeparture ||
        data.CanSaveServices || data.CanSaveTasks || services.Concat(tasks).Any(row => row.CanEdit || row.CanSetEnd || row.CanDelete));

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
                : await PortalWorkLookups.LoadAsync(MasterData, data, occurrenceOnly: false, ct: lifetime.Token);
            services = data.Services.Select(item => PortalWorkDraft.From(item, zone)).ToList();
            tasks = data.Tasks.Select(item => PortalWorkDraft.From(item, zone)).ToList();
            if (!ViewOnly && (data.CanAddServices ?? data.CanSaveServices) && data.CurrentStaffMemberId is { } staffId)
            {
                var staff = lookups.Staff.FirstOrDefault(item => item.Id == staffId);
                foreach (var planned in data.PlannedServices.Where(item => !item.IsAircraftPerLanding
                    && lookups.Services.Any(choice => choice.Id == item.ServiceId)
                    && data.Services.All(existing => existing.ServiceId != item.ServiceId)))
                    services.Add(PortalWorkDraft.FromPlan(planned, staffId, staff, zone));
            }

            flight = data;
            SetArrival(data.ArrivalSubmissions.OrderByDescending(item => item.SubmittedByEmployee.StaffMemberId == data.CurrentStaffMemberId)
                .ThenByDescending(item => item.IsAccepted).FirstOrDefault());
            SetDeparture(data.DepartureSubmissions.OrderByDescending(item => item.SubmittedByEmployee.StaffMemberId == data.CurrentStaffMemberId)
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
        arrivalSource = source;
        arrivalTime = new(zone, source?.Ata);
        aircraftTypeId = source?.AircraftTypeId ?? flight?.AircraftTypeId;
        tailNumber = source?.AircraftTailNumber;
        arrivalReference = source?.CustomerWorkReferenceNumber;
        arrivalRemarks = source?.Remarks;
    }

    private void SetDeparture(PortalFlightDeparture? source)
    {
        departureSource = source;
        departureTime = new(zone, source?.Atd);
        departureRemarks = source?.Remarks;
        signature = source is { HasSignature: true }
            ? new($"departure-signatures/{source.Id}?expectedUpdatedAt={MediaVersion(source.UpdatedAtUtc ?? source.CreatedAtUtc)}")
            : new();
    }

    internal static string MediaVersion(DateTimeOffset stamp) => Uri.EscapeDataString(stamp.ToString("O"));

    private IEnumerable<PortalCatalogChoice> ArrivalOptions() => flight!.ArrivalSubmissions
        .Select(item => new PortalCatalogChoice(item.Id, $"{item.SubmittedByEmployee.FullName} · {zone.Format(item.Ata)}"));

    private IEnumerable<PortalCatalogChoice> DepartureOptions() => flight!.DepartureSubmissions
        .Select(item => new PortalCatalogChoice(item.Id, $"{item.SubmittedByEmployee.FullName} · {zone.Format(item.Atd)}"));

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
        PortalWorkDraft.Trim(arrivalRemarks) != arrivalSource?.Remarks;

    private bool DepartureChanged() => departureTime.Local != Local(departureSource?.Atd) ||
        PortalWorkDraft.Trim(departureRemarks) != departureSource?.Remarks || signature.Bytes is not null;

    private DateTime? Local(DateTimeOffset? value) => value is { } time ? zone.ToLocalDateTime(time) : null;

    private bool WorkChanged() => ArrivalChanged() || DepartureChanged() || services.Concat(tasks).Any(row => row.HasChanges());
    private bool WorkFilesLoading() => signature.IsReading || services.Concat(tasks).Any(row => row.IsReadingFile);

    private bool IsCompletedStep(int index) => IsGuidedEntry && index < step;

    private string StepClass(int index) => index == step ? "portal-step portal-step-active"
        : IsCompletedStep(index) ? "portal-step portal-step-complete" : "portal-step";

    private void ChangeStep(int index)
    {
        if (saving || index == step)
            return;

        if (WorkFilesLoading())
        {
            error = "Wait for the file to finish loading before changing steps.";
            return;
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
                ReadDeparture(validation);
                break;
        }
    }

    private PortalFlightArrivalRequest? ReadArrival(PortalFlightValidation errors, bool requireArrival = false)
    {
        if (!CanEditArrival || !requireArrival && !ArrivalChanged())
            return null;

        errors.Step = 0;
        var ata = arrivalTime.Read("Actual time of arrival", errors, ArrivalTimeField, required: true);
        if (aircraftTypeId is null || aircraftTypeId == Guid.Empty)
            errors.Add(AircraftField, "Aircraft type is required.");
        if (string.IsNullOrWhiteSpace(tailNumber))
            errors.Add(TailField, "Tail number is required.");
        if (RequiresArrivalRemarks && string.IsNullOrWhiteSpace(arrivalRemarks))
            errors.Add(ArrivalRemarksField, "Describe the Unknown customer in Arrival remarks.");

        return new(ata.GetValueOrDefault(), aircraftTypeId.GetValueOrDefault(), tailNumber?.Trim() ?? string.Empty,
            PortalWorkDraft.Trim(arrivalReference), PortalWorkDraft.Trim(arrivalRemarks), false, arrivalSource?.Id);
    }

    private PortalFlightDepartureRequest? ReadDeparture(PortalFlightValidation errors)
    {
        if (!CanEditDeparture || !DepartureChanged())
            return null;

        errors.Step = 3;
        var atd = departureTime.Read("Actual time of departure", errors, DepartureTimeField, required: true);
        var signedAt = signature.SigningTime?.Read("Signed at", errors, SignatureTimeField, required: true);
        return new(atd.GetValueOrDefault(), signature.Bytes, signedAt,
            PortalWorkDraft.Trim(departureRemarks), false, departureSource?.Id);
    }

    private PortalServiceChange[] ReadServices(PortalFlightValidation errors)
    {
        errors.Step = 1;
        return services.Select((row, index) => (row, index)).Where(pair => pair.row.HasChanges())
            .Select(pair => pair.row.ToServiceChange(pair.index + 1, errors)).ToArray();
    }

    private PortalTaskChange[] ReadTasks(PortalFlightValidation errors)
    {
        errors.Step = 2;
        return tasks.Select((row, index) => (row, index)).Where(pair => pair.row.HasChanges())
            .Select(pair => pair.row.ToTaskChange(pair.index + 1, errors)).ToArray();
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
                DialogService.Close(false);
            return;
        }

        requireArrivalForValidation = requireArrivalForValidation && validation.HasErrorsInStep(0);
        validation = new();
        var arrival = ReadArrival(validation, requireArrivalForValidation);
        var serviceChanges = ReadServices(validation);
        var taskChanges = ReadTasks(validation);
        var departure = ReadDeparture(validation);
        if (validation.Count > 0)
        {
            FocusFirstInvalidStep();
            return;
        }

        saving = true;
        try
        {
            var acknowledged = false;
            if ((arrival is not null || departure is not null) && flight.CancellationSubmissions.Count > 0)
            {
                acknowledged = await DialogService.Confirm(
                    "Another employee reported this flight as canceled. Save your operational report?",
                    "Conflicting flight report", new ConfirmOptions { OkButtonText = "Save report", CancelButtonText = "Go back" }) == true;
                if (!acknowledged)
                    return;
            }

            var request = new PortalWorkBatchRequest(
                arrival is null ? null : arrival with { AcknowledgeConflictingSubmission = acknowledged },
                departure is null ? null : departure with { AcknowledgeConflictingSubmission = acknowledged },
                serviceChanges, taskChanges);
            await Operations.SavePortalFlightWorkAsync(FlightId, request, flight.RowVersion, lifetime.Token);
            DialogService.Close(true);
        }
        catch (ApiException ex)
        {
            error = ex.ToDisplayMessage("Could not save the flight work.");
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

    private void FocusFirstInvalidStep()
    {
        step = validation.FirstInvalidStep;
        validationFocusAttempt++;
    }

    private async Task OpenReturnToRampAsync(Guid? occurrenceId)
    {
        if (saving)
            return;

        if (WorkChanged() || WorkFilesLoading())
        {
            error = "Save your flight work before opening a return to ramp.";
            return;
        }

        var result = await DialogService.OpenAsync<PortalReturnToRampDialog>(occurrenceId is null ? "Add RTR" : "Return to ramp",
            new Dictionary<string, object?>
            {
                [nameof(PortalReturnToRampDialog.FlightId)] = FlightId,
                [nameof(PortalReturnToRampDialog.OccurrenceId)] = occurrenceId,
                [nameof(PortalReturnToRampDialog.ViewOnly)] = ViewOnly
            },
            new DialogOptions { Width = "900px", ShowClose = false, CloseDialogOnEsc = false, CloseDialogOnOverlayClick = false });
        if (result is true)
            DialogService.Close(true);
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
            DialogService.Close(false);
    }

    private async Task<bool> ConfirmDiscardAsync() => await DialogService.Confirm(
        "Discard the unsaved changes?", "Discard changes", new ConfirmOptions { OkButtonText = "Discard", CancelButtonText = "Keep editing" }) == true;

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        lifetime.Dispose();
    }
}
