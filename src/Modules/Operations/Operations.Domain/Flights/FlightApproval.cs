using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

public enum FlightApprovalStatus
{
    NotApproved = 0,
    Approved = 1,
    Unlocked = 2,
    PendingRtr = 3
}

public sealed partial class Flight
{
    public const int MaxApproverDisplayNameLength = 150;
    public static readonly TimeSpan AutoCloseDelay = TimeSpan.FromHours(1);

    public FlightApprovalStatus ApprovalStatus { get; private set; }
    public bool IsAutoClosed { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public string? ApprovedByDisplayName { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public int ApprovedReturnToRampSequence { get; private set; }
    public int? ApprovalSequence { get; private set; }
    public string? ApprovalNumber { get; private set; }

    public bool IsApprovalLocked => ApprovalStatus is FlightApprovalStatus.Approved or FlightApprovalStatus.PendingRtr;

    /// <summary>Load all submissions, work records, and RTRs before approving the complete flight.</summary>
    public Result Approve(Guid approvedByUserId, string? approvedByDisplayName, DateTimeOffset now, int approvalSequence)
    {
        if (ApprovalStatus == FlightApprovalStatus.Approved)
            return Error.Conflict("This flight is already approved.", "Operations.FlightApproval.AlreadyApproved");

        var displayName = approvedByDisplayName?.Trim();
        if (approvedByUserId == Guid.Empty || string.IsNullOrEmpty(displayName) || displayName.Length > MaxApproverDisplayNameLength || now == default)
            return Error.Validation("An identified approver and approval time are required.", "Operations.FlightApproval.InvalidApprover");

        var validation = ValidateApproval();
        if (validation.IsFailure)
            return validation;

        return RecordApproval(approvedByUserId, displayName, now, approvalSequence);
    }

    /// <summary>Requires the planned services and confirmation that all operational collections are empty.</summary>
    public Result AutoClose(DateTimeOffset now, int approvalSequence)
    {
        if (ApprovalStatus != FlightApprovalStatus.NotApproved || !IsPerLanding ||
            OperationType.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType ||
            now - Schedule.Std < AutoCloseDelay || HasOperationalRecords())
            return Error.Conflict("The flight is no longer eligible for automatic closure.", "Operations.FlightApproval.AutoCloseNotEligible");

        var approval = RecordApproval(null, "System", now, approvalSequence);
        if (approval.IsSuccess)
            IsAutoClosed = true;

        return approval;
    }

    private Result RecordApproval(Guid? approvedByUserId, string displayName, DateTimeOffset now, int approvalSequence)
    {
        if (ApprovalNumber is null)
        {
            var number = WorkOrderNumber.Format(Station.IataCode, approvalSequence);
            if (number.IsFailure)
                return number.Error;

            ApprovalSequence = approvalSequence;
            ApprovalNumber = number.Value;
        }

        ApprovalStatus = FlightApprovalStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedByDisplayName = displayName;
        ApprovedAtUtc = now.ToUniversalTime();
        ApprovedReturnToRampSequence = LastReturnToRampSequence;
        UpdatedAtUtc = ApprovedAtUtc;
        return Result.Success();
    }

    public Result Unlock(DateTimeOffset now)
    {
        if (ApprovalStatus != FlightApprovalStatus.Approved)
            return Error.Conflict("Only an approved flight can be unlocked.", "Operations.FlightApproval.UnlockNotAllowed");

        ApprovalStatus = FlightApprovalStatus.Unlocked;
        IsAutoClosed = false;
        ApprovalSequence = null;
        ApprovalNumber = null;
        UpdatedAtUtc = now.ToUniversalTime();
        return Result.Success();
    }

    public Result EnsureApprovalAllowsEdit() => IsApprovalLocked
        ? Error.Conflict("This flight is locked. Unlock it before changing its recorded details.", "Operations.FlightApproval.Locked")
        : Result.Success();

    /// <summary>Protects previously approved occurrences while a flight remains locked.</summary>
    public Result EnsureApprovalAllowsReturnToRamp(Guid occurrenceId)
    {
        var existing = _returnToRamps.SingleOrDefault(item => item.Id == occurrenceId);
        return IsApprovalLocked && existing is not null && existing.Sequence <= ApprovedReturnToRampSequence
            ? Error.Conflict("This return to ramp is approved and locked.", "Operations.FlightApproval.ReturnToRampLocked")
            : Result.Success();
    }

    private Result ValidateApproval()
    {
        if (_cancellationSubmissions.Count > 0)
        {
            return _arrivalSubmissions.Count > 0 || _departureSubmissions.Count > 0 ||
                _services.Count > 0 || _tasks.Count > 0 || _returnToRamps.Count > 0
                    ? Error.Conflict("This flight has both work and cancellation reports.", "Operations.FlightApproval.ConflictingReports")
                    : Result.Success();
        }

        if (_arrivalSubmissions.Count != 1 || _departureSubmissions.Count != 1)
            return Error.Validation("A completed flight needs one arrival and one departure report.", "Operations.FlightApproval.ActualTimesRequired");

        var arrival = _arrivalSubmissions[0].Details.Ata;
        var departure = _departureSubmissions[0].Details.Atd;
        if (arrival >= departure)
            return Error.Validation("Actual arrival must be before actual departure.", "Operations.FlightApproval.ActualTimesInvalid");

        var normalWork = ValidateApprovalWork(null, arrival, departure, "Flight");
        if (normalWork.IsFailure)
            return normalWork;

        foreach (var occurrence in _returnToRamps)
        {
            var label = $"Return to ramp {occurrence.Sequence}";
            if (occurrence.DepartureDetails is not { } rtrDeparture || rtrDeparture.DepartedAtUtc <= occurrence.ArrivalDetails.ArrivedAtUtc)
                return Error.Validation($"{label} needs a departure after its arrival.", "Operations.FlightApproval.ReturnToRampTimesInvalid");

            var rtrWork = ValidateApprovalWork(occurrence.Id, occurrence.ArrivalDetails.ArrivedAtUtc, rtrDeparture.DepartedAtUtc, label);
            if (rtrWork.IsFailure)
                return rtrWork;
        }

        return Result.Success();
    }

    private Result ValidateApprovalWork(Guid? occurrenceId, DateTimeOffset arrival, DateTimeOffset departure, string label)
    {
        foreach (var service in _services.Where(item => item.ReturnToRampId == occurrenceId))
        {
            var result = ValidateApprovalActivity(service.Window, service.Performers.Select(item => item.Window),
                arrival, departure, $"{label} service {service.Service.Name}");
            if (result.IsFailure)
                return result;
        }

        foreach (var task in _tasks.Where(item => item.ReturnToRampId == occurrenceId))
        {
            var taskLabel = $"{label} task";
            var result = ValidateApprovalActivity(task.Window, task.Performers.Select(item => item.Window),
                arrival, departure, taskLabel);
            if (result.IsFailure)
                return result;

            var resources = task.Tools.Select(item => item.Usage)
                .Concat(task.Materials.Select(item => item.Usage))
                .Concat(task.GeneralSupports.Select(item => item.Usage));
            if (resources.Any(usage => usage.Quantity is null &&
                (usage.FromUtc is not { } start || usage.ToUtc is not { } end || !IsContained(start, end, task.Window))))
                return Error.Validation($"{taskLabel} has an incomplete or out-of-bounds duration resource.",
                    "Operations.FlightApproval.ResourceWindowInvalid");
        }

        return Result.Success();
    }

    private static Result ValidateApprovalActivity(TimeWindow window, IEnumerable<TimeWindow> performers,
        DateTimeOffset arrival, DateTimeOffset departure, string label)
    {
        if (window.ToUtc is not { } end || window.FromUtc >= end || window.FromUtc < arrival || end > departure)
            return Error.Validation($"{label} must start before it ends and stay within arrival and departure.",
                "Operations.FlightApproval.ActivityWindowInvalid");

        var employeePeriods = performers.ToArray();
        if (employeePeriods.Length == 0 || employeePeriods.Any(period =>
            period.ToUtc is not { } periodEnd || !IsContained(period.FromUtc, periodEnd, window)))
            return Error.Validation($"{label} needs completed performer periods within its work period.",
                "Operations.FlightApproval.PerformerWindowInvalid");

        return Result.Success();
    }

    private static bool IsContained(DateTimeOffset start, DateTimeOffset end, TimeWindow outer) =>
        start >= outer.FromUtc && start <= end && end <= outer.ToUtc;
}
