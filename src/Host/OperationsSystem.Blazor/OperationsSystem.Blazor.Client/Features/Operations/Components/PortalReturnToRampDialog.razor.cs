using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.State;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public partial class PortalReturnToRampDialog
{
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
    private ElementReference validationContainer;
    private int validationFocusAttempt;
    private string? error;
    private bool loading = true;
    private bool saving;

    private bool CanEdit => !ViewOnly && flight is { CanSaveReturnsToRamp: true } data &&
        (occurrence?.CanEdit ?? (data.DepartureSubmissions.Count > 0 && data.ReturnToRamps.All(item => !item.IsOpen)));

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        loading = true;
        error = null;
        try
        {
            var data = await Operations.GetPortalFlightOperationsAsync(FlightId, lifetime.Token);
            occurrence = data.ReturnToRamps.SingleOrDefault(item => item.Id == OccurrenceId);
            if (OccurrenceId.HasValue && occurrence is null)
            {
                error = "The return to ramp is no longer available.";
                return;
            }

            zone = StationTimeZone.FromId(data.StationTimeZoneId);
            lookups = ViewOnly || !data.CanSaveReturnsToRamp
                ? new([], [], [], [], [], [], [], false)
                : await PortalWorkLookups.LoadAsync(MasterData, data, occurrenceOnly: true, ct: lifetime.Token);
            arrivalTime = new(zone, occurrence?.ArrivedAtUtc);
            departureTime = new(zone, occurrence?.DepartedAtUtc);
            arrivalRemarks = occurrence?.ArrivalRemarks;
            departureRemarks = occurrence?.DepartureRemarks;
            services = occurrence?.Services.Select(item => PortalWorkDraft.From(item, zone)).ToList() ?? [];
            tasks = occurrence?.Tasks.Select(item => PortalWorkDraft.From(item, zone)).ToList() ?? [];
            signature = occurrence is { HasSignature: true } report
                ? new($"returns-to-ramp/{report.Id}/signature?expectedUpdatedAt={PortalFlightWorkDialog.MediaVersion(report.UpdatedAtUtc ?? report.CreatedAtUtc)}")
                : new();
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

    private bool WorkChanged() => arrivalTime.HasChanged || departureTime.HasChanged ||
        PortalWorkDraft.Trim(arrivalRemarks) != occurrence?.ArrivalRemarks ||
        PortalWorkDraft.Trim(departureRemarks) != occurrence?.DepartureRemarks ||
        signature.Bytes is not null || signature.RemoveSaved || services.Concat(tasks).Any(row => row.HasChanges());

    private void RefreshValidation()
    {
        if (validation.Count == 0)
            return;

        validation.ClearStep(0);
        ReadRequest();
    }

    private async Task SaveAsync()
    {
        if (flight is null || !CanEdit || saving)
            return;

        error = null;
        validation = new();
        if (signature.IsReading || services.Concat(tasks).Any(row => row.IsReadingFile))
        {
            error = "Wait for the selected file to finish loading.";
            return;
        }

        if (occurrence is not null && !WorkChanged())
        {
            DialogService.Close(false);
            return;
        }

        var request = ReadRequest();
        if (validation.Count > 0)
        {
            validationFocusAttempt++;
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
        var arrived = arrivalTime.Read("RTR arrival time", validation, ArrivalTimeField, required: true);
        var departed = departureTime.Read("RTR departure time", validation, DepartureTimeField, required: occurrence is { IsOpen: false }
            || !string.IsNullOrWhiteSpace(departureRemarks) || signature.Bytes is not null);
        if (arrived > departed)
            validation.Add(DepartureTimeField, "Return-to-ramp departure cannot be before arrival.");
        if (PortalWorkDraft.Trim(arrivalRemarks)?.Length > 2000)
            validation.Add(ArrivalRemarksField, "RTR arrival remarks must be at most 2,000 characters.");
        if (PortalWorkDraft.Trim(departureRemarks)?.Length > 2000)
            validation.Add(DepartureRemarksField, "RTR departure remarks must be at most 2,000 characters.");

        var signedAt = signature.SigningTime?.Read("Signed at", validation, SignatureTimeField, required: true);
        var closing = departed.HasValue;
        var serviceChanges = services.Select((row, index) => (row, index)).Where(pair => !pair.row.Deleted)
            .Select(pair => pair.row.ToServiceChange(pair.index + 1, validation, closing, inReturnToRamp: true)).ToArray();
        var taskChanges = tasks.Select((row, index) => (row, index)).Where(pair => !pair.row.Deleted)
            .Select(pair => pair.row.ToTaskChange(pair.index + 1, validation, closing, inReturnToRamp: true)).ToArray();
        if (closing && serviceChanges.Length == 0 && taskChanges.Length == 0)
            validation.Add(DepartureTimeField, "Record at least one service or task before closing the return to ramp.");

        var departure = departed is { } end
            ? new PortalReturnToRampDepartureRequest(end, PortalWorkDraft.Trim(departureRemarks), signature.Bytes,
                signedAt, signature.RemoveSaved)
            : null;
        return new(new(arrived.GetValueOrDefault(), PortalWorkDraft.Trim(arrivalRemarks)), departure, serviceChanges, taskChanges);
    }

    private void CloseWithoutSaving() => DialogService.Close(false);

    private async Task CloseAsync()
    {
        if (signature.IsReading || services.Concat(tasks).Any(row => row.IsReadingFile))
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
