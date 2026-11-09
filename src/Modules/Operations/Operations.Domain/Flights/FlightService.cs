using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>An actual service occurrence, with its employees' work periods and file references.</summary>
public sealed class FlightService : Entity<Guid>
{
    public const int MaxAttachments = 10;

    private readonly List<FlightServicePerformer> _performers = [];
    private readonly List<FlightServiceAttachment> _attachments = [];

    private FlightService() { }

    public Guid FlightId { get; private set; }
    public Guid? ReturnToRampId { get; private set; }
    public ServiceSnapshot Service { get; private set; } = null!;
    public string? CustomerReferenceNumber { get; private set; }
    public TimeWindow Window { get; private set; } = null!;
    public Guid? EndedByUserId { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public IReadOnlyList<FlightServicePerformer> Performers => _performers.AsReadOnly();
    public IReadOnlyList<FlightServiceAttachment> Attachments => _attachments.AsReadOnly();

    internal static Result<FlightService> Create(Guid flightId, ServiceSnapshot? service, string? customerReferenceNumber,
        TimeWindow? window, string? notes, Guid userId, DateTimeOffset now, Guid? returnToRampId = null)
    {
        var check = ValidateDetails(service, customerReferenceNumber, window, notes);
        if (check.IsFailure)
            return check.Error;

        return new FlightService
        {
            Id = Guid.NewGuid(),
            FlightId = flightId,
            ReturnToRampId = returnToRampId,
            Service = new ServiceSnapshot(service!.ServiceId, service.Name),
            CustomerReferenceNumber = NormalizeOptional(customerReferenceNumber),
            Window = window!.Copy(),
            EndedByUserId = window.ToUtc.HasValue ? userId : null,
            Notes = NormalizeOptional(notes),
            CreatedByUserId = userId,
            CreatedAtUtc = now
        };
    }

    internal Result<bool> UpdateDetails(ServiceSnapshot? service, string? customerReferenceNumber, TimeWindow? window, string? notes,
        Guid userId)
    {
        var check = ValidateDetails(service, customerReferenceNumber, window, notes);
        if (check.IsFailure)
            return check.Error;

        foreach (var performer in _performers)
        {
            var period = OperationalWorkValidation.EmployeeWindow(performer.Window, window!);
            if (period.IsFailure)
                return period.Error;
        }

        var reference = NormalizeOptional(customerReferenceNumber);
        var normalizedNotes = NormalizeOptional(notes);
        if (Service == service && Window == window && CustomerReferenceNumber == reference && Notes == normalizedNotes)
            return false;

        if (Window.ToUtc != window!.ToUtc)
            EndedByUserId = window.ToUtc.HasValue ? userId : null;

        Service = new ServiceSnapshot(service!.ServiceId, service.Name);
        CustomerReferenceNumber = reference;
        Window = window.Copy();
        Notes = normalizedNotes;
        return true;
    }

    internal Result<FlightServicePerformer> AddPerformer(StaffMemberSnapshot? employee, TimeWindow? window)
    {
        var check = ValidatePerformer(employee, window);
        if (check.IsFailure)
            return check.Error;

        if (_performers.Any(item => item.Employee.StaffMemberId == employee!.StaffMemberId))
            return OperationalWorkValidation.DuplicateEmployee();

        var performer = new FlightServicePerformer(Guid.NewGuid(), Id, employee!, window!);
        _performers.Add(performer);
        return performer;
    }

    internal Result<bool> UpdatePerformer(Guid performerId, StaffMemberSnapshot? employee, TimeWindow? window)
    {
        var performer = _performers.SingleOrDefault(item => item.Id == performerId);
        if (performer is null)
            return Error.NotFound("Service performer was not found.", "Operations.FlightService.PerformerNotFound");
        var check = ValidatePerformer(employee, window);
        if (check.IsFailure)
            return check.Error;
        if (_performers.Any(item => item.Id != performerId && item.Employee.StaffMemberId == employee!.StaffMemberId))
            return OperationalWorkValidation.DuplicateEmployee();
        if (performer.Employee == employee && performer.Window == window)
            return false;

        performer.Update(employee!, window!);
        return true;
    }

    internal Result RemovePerformer(Guid performerId)
    {
        var performer = _performers.SingleOrDefault(item => item.Id == performerId);
        if (performer is null)
            return Error.NotFound("Service performer was not found.", "Operations.FlightService.PerformerNotFound");
        _performers.Remove(performer);
        return Result.Success();
    }

    internal Result<FlightServiceAttachment> AddAttachment(AttachmentDetails? details, Guid userId, DateTimeOffset now)
    {
        if (details is null)
            return Error.Validation("Attachment details are required.", "Operations.FlightService.AttachmentRequired");

        if (_attachments.Count >= MaxAttachments)
            return Error.Validation("A service can have at most 10 attachments.", "Operations.WorkOrder.AttachmentLimitExceeded");

        var attachment = new FlightServiceAttachment(Guid.NewGuid(), Id, details, userId, now);
        _attachments.Add(attachment);
        return attachment;
    }

    internal Result<FlightServiceAttachment> RemoveAttachment(Guid attachmentId)
    {
        var attachment = _attachments.SingleOrDefault(item => item.Id == attachmentId);
        if (attachment is null)
            return Error.NotFound("Service attachment was not found.", "Operations.FlightService.AttachmentNotFound");
        _attachments.Remove(attachment);
        return attachment;
    }

    internal void MarkUpdated(Guid userId, DateTimeOffset now)
    {
        UpdatedByUserId = userId;
        UpdatedAtUtc = now;
    }

    private static Result ValidateDetails(ServiceSnapshot? service, string? customerReferenceNumber, TimeWindow? window, string? notes)
    {
        if (service is null || service.ServiceId == Guid.Empty)
            return Error.Validation("Service is required.", "Operations.FlightService.ServiceRequired");
        if (window is null)
            return Error.Validation("Service time window is required.", "Operations.FlightService.WindowRequired");
        if (service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService)
            return Error.Validation("Aircraft Per Landing cannot be selected as an actual service.", "Operations.WorkOrder.PerLandingLineNotAllowed");
        var reference = OperationalWorkValidation.ReferenceNumber(customerReferenceNumber);
        if (reference.IsFailure)
            return reference;
        return OperationalWorkValidation.Description(service.ServiceId, WellKnownMasterDataIds.UnknownService, notes, "Service");
    }

    private Result ValidatePerformer(StaffMemberSnapshot? employee, TimeWindow? window)
    {
        if (employee is null || employee.StaffMemberId == Guid.Empty)
            return Error.Validation("Performing employee is required.", "Operations.FlightService.EmployeeRequired");
        if (window is null)
            return Error.Validation("Employee time window is required.", "Operations.FlightService.WindowRequired");
        return OperationalWorkValidation.EmployeeWindow(window, Window);
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
