using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.State;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public partial class PortalReturnToRampDialog
{
    private static readonly string[] StepLabels = ["Arrival", "Services", "Tasks", "Departure"];
    private const int ArrivalStep = 0;
    private const int ServicesStep = 1;
    private const int TasksStep = 2;
    private const int DepartureStep = 3;
    private const string ArrivalTimeField = "rtr-arrival-time";
    private const string ArrivalRemarksField = "rtr-arrival-remarks";
    private const string DepartureTimeField = "rtr-departure-time";
    private const string DepartureRemarksField = "rtr-departure-remarks";
    private const string SignatureTimeField = "rtr-signature-time";

    [Inject] private OperationsApiClient Operations { get; set; } = default!;
    [Inject] private MasterDataApiClient MasterData { get; set; } = default!;
    [Inject] private DialogService DialogService { get; set; } = default!;

    [Parameter, EditorRequired] public Guid FlightId { get; set; }
    [Parameter] public Guid? OccurrenceId { get; set; }
    [Parameter] public bool ViewOnly { get; set; }
    [Parameter] public bool AdministratorCorrections { get; set; }
    [Parameter] public PortalFlightOperations? DraftFlight { get; set; }
    [Parameter] public PortalReturnToRampDraft? Draft { get; set; }
    [Parameter] public int DraftSequence { get; set; }
    [Parameter] public bool RequireClosedDraft { get; set; }

    private readonly CancellationTokenSource lifetime = new();
    private readonly Guid newOccurrenceId = Guid.NewGuid();
    private PortalFlightOperations? flight;
    private PortalFlightReturnToRamp? occurrence;
    private StationTimeZone zone = default!;
    private PortalWorkLookups lookups = default!;
    private PortalOperationTime arrivalTime = default!;
    private PortalOperationTime departureTime = default!;
    private string? arrivalRemarks;
    private string? departureRemarks;
    private PortalSignatureDraft signature = new();
    private List<PortalWorkDraft> services = [];
    private List<PortalWorkDraft> tasks = [];
    private PortalFlightValidation validation = new();
    private int validationFocusAttempt;
    private int step;
    private bool validateAllSteps;
    private string? error;
    private bool loading = true;
    private bool saving;
    private bool arrivalUnlocked;
    private bool departureUnlocked;

    private bool CanEdit => !ViewOnly && (AdministratorCorrections
        ? occurrence?.CanEdit == true
        : DraftFlight is not null || flight is { CanSaveReturnsToRamp: true } data &&
            (occurrence?.CanEdit ?? (data.DepartureSubmissions.Count > 0 && data.ReturnToRamps.All(item => !item.IsOpen))));
    private bool CanEditDeparture => CanEdit && (!AdministratorCorrections || occurrence?.DepartedAtUtc is not null);

    private bool ArrivalFieldsLocked => occurrence is not null && !arrivalUnlocked;
    private bool DepartureFieldsLocked => occurrence?.DepartedAtUtc is not null && !departureUnlocked;
    private bool RequiresDeparture => RequireClosedDraft || occurrence is { IsOpen: false } ||
        !string.IsNullOrWhiteSpace(departureRemarks) || signature.Bytes is not null;

    private bool IsGuidedEntry => CanEdit && occurrence is null && Draft is null;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        loading = true;
        error = null;
        try
        {
            var data = DraftFlight ?? await Operations.GetPortalFlightOperationsAsync(FlightId, lifetime.Token);
            occurrence = data.ReturnToRamps.SingleOrDefault(item => item.Id == OccurrenceId);
            if ((OccurrenceId.HasValue || AdministratorCorrections) && occurrence is null)
            {
                error = "The return to ramp is no longer available.";
                return;
            }

            zone = StationTimeZone.FromId(data.StationTimeZoneId);
            arrivalUnlocked = false;
            departureUnlocked = false;
            var loadCatalogs = AdministratorCorrections ? occurrence?.CanEdit == true : DraftFlight is not null || data.CanSaveReturnsToRamp;
            lookups = ViewOnly || !loadCatalogs
                ? new([], [], [], [], [], [], [], false)
                : await PortalWorkLookups.LoadAsync(MasterData, data with { CanSaveReturnsToRamp = true },
                    occurrenceOnly: true, administratorCorrections: AdministratorCorrections, ct: lifetime.Token);
            arrivalTime = new(zone, occurrence?.ArrivedAtUtc);
            departureTime = new(zone, occurrence?.DepartedAtUtc);
            arrivalRemarks = occurrence?.ArrivalRemarks;
            departureRemarks = occurrence?.DepartureRemarks;
            services = occurrence?.Services.Select(item => PortalWorkDraft.From(item, zone)).ToList() ?? [];
            tasks = occurrence?.Tasks.Select(item => PortalWorkDraft.From(item, zone)).ToList() ?? [];
            signature = occurrence is { HasSignature: true } report
                ? new($"returns-to-ramp/{report.Id}/signature?expectedUpdatedAt={PortalFlightWorkDialog.MediaVersion(report.UpdatedAtUtc ?? report.CreatedAtUtc)}")
                : new();
            if (Draft is not null)
                LoadDraft(Draft);
            flight = data;
        }
        catch (ApiException ex)
        {
            error = ex.ToDisplayMessage("Could not load the return to ramp.");
        }
        catch (JSException)
        {
            error = "Could not load the return to ramp. Try again.";
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

    private void LoadDraft(PortalReturnToRampDraft draft)
    {
        var input = draft.Request;
        arrivalTime = new(zone, input.Arrival.ArrivedAtUtc);
        departureTime = new(zone, input.Departure?.DepartedAtUtc);
        arrivalRemarks = input.Arrival.Remarks;
        departureRemarks = input.Departure?.Remarks;
        signature = new()
        {
            Bytes = input.Departure?.SignatureContent,
            FileName = draft.SignatureFileName,
            SigningTime = input.Departure?.SignatureSignedAtUtc is { } signed ? new(zone, signed) : null
        };
        services = input.Services.Select(change =>
        {
            var value = change.Input!;
            var work = new PortalWorkDraft(change.RecordId, false, zone, value.StartUtc, value.EndUtc)
            {
                ServiceId = value.ServiceId, Reference = value.CustomerReferenceNumber, Notes = value.Notes
            };
            ReadWork(work, value.Performers, value.Attachments);
            return work;
        }).ToList();
        tasks = input.Tasks.Select(change =>
        {
            var value = change.Input!;
            var work = new PortalWorkDraft(change.RecordId, true, zone, value.StartUtc, value.EndUtc)
            {
                TaskType = value.TaskType, AtaChapterId = value.AtaChapterId, Reference = value.CustomerReferenceNumber, Notes = value.Notes
            };
            ReadWork(work, value.Performers, value.Attachments);
            work.Tools.AddRange(ReadResources(value.Tools, lookups.Tools));
            work.Materials.AddRange(ReadResources(value.Materials, lookups.Materials));
            work.GeneralSupports.AddRange(ReadResources(value.GeneralSupports, lookups.Supports));
            return work;
        }).ToList();

        void ReadWork(PortalWorkDraft work, IReadOnlyList<PortalFlightPerformerRequest> performers,
            IReadOnlyList<PortalFlightAttachmentRequest> attachments)
        {
            foreach (var performer in performers)
            {
                var staff = lookups.Staff.FirstOrDefault(choice => choice.Id == performer.StaffMemberId);
                work.Performers.Add(new(performer.Id, zone, performer.StartUtc, performer.EndUtc)
                {
                    StaffMemberId = performer.StaffMemberId, DisplayName = staff?.Label, FullName = staff?.FullName, Notes = performer.Notes
                });
            }
            work.Attachments.AddRange(attachments.Select(file =>
                new PortalAttachmentDraft(file.Id, file.Kind, file.FileName, file.ContentType, file.Content?.LongLength ?? 0, null, file.Content)));
        }

        IEnumerable<PortalResourceDraft> ReadResources(IReadOnlyList<PortalFlightResourceRequest> values, IReadOnlyList<PortalCatalogChoice> options) =>
            values.Select(value => new PortalResourceDraft(value.Id, value.ResourceId,
                options.FirstOrDefault(choice => choice.Id == value.ResourceId)?.Label ?? "Saved resource",
                value.Quantity is null ? ResourceCalculationType.Duration : ResourceCalculationType.Quantity, zone, value.StartUtc, value.EndUtc)
            {
                Quantity = value.Quantity, Notes = value.Notes
            });
    }

    private bool WorkChanged() => arrivalTime.HasChanged || departureTime.HasChanged ||
        PortalWorkDraft.Trim(arrivalRemarks) != occurrence?.ArrivalRemarks ||
        PortalWorkDraft.Trim(departureRemarks) != occurrence?.DepartureRemarks ||
        signature.Bytes is not null || signature.RemoveSaved || services.Concat(tasks).Any(row => row.HasChanges());

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

        error = null;
        if (index > step && CanEdit)
        {
            ValidateStep(ArrivalStep);
            if (validation.HasErrorsInStep(ArrivalStep))
            {
                step = ArrivalStep;
                validationFocusAttempt++;
                return;
            }

            if (IsGuidedEntry && step != ArrivalStep)
            {
                ValidateStep(step);
                if (validation.HasErrorsInStep(step))
                {
                    validationFocusAttempt++;
                    return;
                }
            }
        }

        step = index;
    }

    private void NextStep()
    {
        if (IsGuidedEntry)
            ChangeStep(step + 1);
    }

    private void RefreshValidation()
    {
        if (validation.Count == 0)
            return;

        if (validateAllSteps)
        {
            validation = new();
            ReadRequest();
        }
        else if (validation.HasErrorsInStep(step))
        {
            ValidateStep(step);
        }
    }

    private void ValidateStep(int index)
    {
        validation.ClearStep(index);
        arrivalTime.TryReadUtc(out var arrived, out _);
        departureTime.TryReadUtc(out var departed, out _);
        switch (index)
        {
            case ArrivalStep:
                ReadArrival();
                break;
            case ServicesStep:
                ReadServices(arrived, departed);
                break;
            case TasksStep:
                ReadTasks(arrived, departed);
                break;
            case DepartureStep:
                ReadDeparture(arrived);
                break;
        }
    }

    private async Task SaveAsync()
    {
        if (flight is null || !CanEdit || saving)
            return;

        error = null;
        validation = new();
        if (WorkFilesLoading())
        {
            error = "Wait for the selected file to finish loading.";
            return;
        }

        if (occurrence is not null && !WorkChanged())
        {
            DialogService.Close(false);
            return;
        }

        validateAllSteps = true;
        var request = ReadRequest();
        if (validation.Count > 0)
        {
            step = validation.FirstInvalidStep;
            validationFocusAttempt++;
            return;
        }

        if (DraftFlight is not null)
        {
            DialogService.Close(new PortalReturnToRampDraft(Draft?.Id ?? newOccurrenceId, DraftSequence, request, signature.FileName));
            return;
        }

        saving = true;
        try
        {
            await Operations.SavePortalReturnToRampAsync(FlightId, OccurrenceId ?? newOccurrenceId, request, flight.RowVersion, lifetime.Token);
            DialogService.Close(true);
        }
        catch (ApiException ex)
        {
            error = ex.ToDisplayMessage("Could not save the return to ramp.");
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

    private PortalReturnToRampRequest ReadRequest()
    {
        var arrived = ReadArrival();
        var departure = ReadDeparture(arrived);
        var serviceChanges = ReadServices(arrived, departure?.DepartedAtUtc);
        var taskChanges = ReadTasks(arrived, departure?.DepartedAtUtc);
        return new(new(arrived.GetValueOrDefault(), PortalWorkDraft.Trim(arrivalRemarks)), departure, serviceChanges, taskChanges);
    }

    private DateTimeOffset? ReadArrival()
    {
        validation.Step = ArrivalStep;
        var arrived = arrivalTime.Read("RTR arrival time", validation, ArrivalTimeField, required: true);
        if (PortalWorkDraft.Trim(arrivalRemarks)?.Length > 2000)
            validation.Add(ArrivalRemarksField, "RTR arrival remarks must be at most 2,000 characters.");
        return arrived;
    }

    private PortalReturnToRampDepartureRequest? ReadDeparture(DateTimeOffset? arrived)
    {
        if (AdministratorCorrections && occurrence?.DepartedAtUtc is null)
            return null;

        validation.Step = DepartureStep;
        var departed = departureTime.Read("RTR departure time", validation, DepartureTimeField, required: RequiresDeparture);
        if (arrived > departed)
            validation.Add(DepartureTimeField, "Return-to-ramp departure cannot be before arrival.");
        if (PortalWorkDraft.Trim(departureRemarks)?.Length > 2000)
            validation.Add(DepartureRemarksField, "RTR departure remarks must be at most 2,000 characters.");

        var signedAt = signature.SigningTime?.Read("Signed at", validation, SignatureTimeField, required: true);
        if (departed.HasValue && services.All(row => row.Deleted) && tasks.All(row => row.Deleted))
            validation.Add(DepartureTimeField, "Record at least one service or task before closing the return to ramp.");
        return departed is { } end
            ? new(end, PortalWorkDraft.Trim(departureRemarks), signature.Bytes, signedAt, signature.RemoveSaved)
            : null;
    }

    private PortalServiceChange[] ReadServices(DateTimeOffset? arrived, DateTimeOffset? departed)
    {
        validation.Step = ServicesStep;
        PortalWorkDraft.ValidateServicePeriods(services, validation, includeUnchanged: true);
        PortalWorkDraft.ValidateActualBounds(services, validation, arrived, departed, includeUnchanged: true);
        return services.Select((row, index) => (row, index)).Where(pair => !pair.row.Deleted)
            .Select(pair => pair.row.ToServiceChange(pair.index + 1, validation, departed.HasValue)).ToArray();
    }

    private PortalTaskChange[] ReadTasks(DateTimeOffset? arrived, DateTimeOffset? departed)
    {
        validation.Step = TasksStep;
        PortalWorkDraft.ValidateActualBounds(tasks, validation, arrived, departed, includeUnchanged: true);
        return tasks.Select((row, index) => (row, index)).Where(pair => !pair.row.Deleted)
            .Select(pair => pair.row.ToTaskChange(pair.index + 1, validation, departed.HasValue)).ToArray();
    }

    private void CloseWithoutSaving() => DialogService.Close(false);

    private async Task CloseAsync()
    {
        if (WorkFilesLoading())
        {
            error = "Wait for the file to finish loading before closing.";
            return;
        }

        if (!WorkChanged() || await DialogService.Confirm("Discard the unsaved changes?", "Discard changes",
                new ConfirmOptions { OkButtonText = "Discard", CancelButtonText = "Keep editing" }) == true)
            DialogService.Close(false);
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        lifetime.Dispose();
    }
}
