using Operations.Domain.Flights;

namespace Operations.Application.Contracts;

public sealed record SaveReturnToRampArrivalRequest(DateTimeOffset ArrivedAtUtc, string? Remarks = null, Guid? ReceivedByStaffMemberId = null);
public sealed record SaveReturnToRampDepartureRequest(DateTimeOffset DepartedAtUtc, string? Remarks = null,
    byte[]? SignatureContent = null, string? SignatureFileName = null, string? SignatureContentType = null,
    DateTimeOffset? SignatureSignedAtUtc = null, bool RemoveSignature = false, Guid? HandledByStaffMemberId = null);
public sealed record ReturnToRampServiceChange(Guid RecordId, SaveFlightServiceRequest Input);
public sealed record ReturnToRampTaskChange(Guid RecordId, SaveFlightTaskRequest Input);
public sealed record SaveFlightReturnToRampRequest(SaveReturnToRampArrivalRequest Arrival,
    SaveReturnToRampDepartureRequest? Departure = null, IReadOnlyList<ReturnToRampServiceChange>? Services = null,
    IReadOnlyList<ReturnToRampTaskChange>? Tasks = null);

public sealed record FlightReturnToRampDto(Guid Id, int Sequence, DateTimeOffset ArrivedAtUtc, string? ArrivalRemarks,
    Guid ReceivedByStaffMemberId, string ReceivedByFullName, DateTimeOffset? DepartedAtUtc, string? DepartureRemarks,
    Guid? HandledByStaffMemberId, string? HandledByFullName, bool HasSignature, DateTimeOffset? SignatureSignedAtUtc,
    Guid CreatedByUserId, DateTimeOffset CreatedAtUtc, Guid? UpdatedByUserId, DateTimeOffset? UpdatedAtUtc,
    string? CreatedByDisplayName, string? UpdatedByDisplayName, bool CanEdit, bool IsOpen,
    IReadOnlyList<FlightServiceDto> Services, IReadOnlyList<FlightTaskDto> Tasks)
{
    internal static FlightReturnToRampDto Map(Flight flight, FlightReturnToRamp occurrence, Guid? userId,
        bool canOperate, IReadOnlyList<Guid>? allowedServices, IReadOnlyDictionary<Guid, string>? names, bool creatorOnlyWorkDelete, bool unrestrictedServices) => new(
        occurrence.Id, occurrence.Sequence, occurrence.ArrivalDetails.ArrivedAtUtc, occurrence.ArrivalDetails.Remarks,
        occurrence.ArrivalDetails.ReceivedByEmployee.StaffMemberId, occurrence.ArrivalDetails.ReceivedByEmployee.FullName,
        occurrence.DepartureDetails?.DepartedAtUtc, occurrence.DepartureDetails?.Remarks,
        occurrence.DepartureDetails?.HandledByEmployee.StaffMemberId, occurrence.DepartureDetails?.HandledByEmployee.FullName,
        occurrence.DepartureDetails?.CustomerSignature is not null, occurrence.DepartureDetails?.CustomerSignature?.SignedAtUtc,
        occurrence.CreatedByUserId, occurrence.CreatedAtUtc, occurrence.UpdatedByUserId, occurrence.UpdatedAtUtc,
        names?.GetValueOrDefault(occurrence.CreatedByUserId), occurrence.UpdatedByUserId is { } updater ? names?.GetValueOrDefault(updater) : null,
        canOperate, occurrence.IsOpen,
        flight.ServiceRecords.Where(s => s.ReturnToRampId == occurrence.Id).OrderBy(s => s.CreatedAtUtc).ThenBy(s => s.Id)
            .Select(s => FlightServiceDto.Map(s, userId, canOperate && (unrestrictedServices || allowedServices?.Contains(s.Service.ServiceId) == true), canOperate,
                names?.GetValueOrDefault(s.CreatedByUserId), s.EndedByUserId is { } closer ? names?.GetValueOrDefault(closer) : null,
                s.UpdatedByUserId is { } updated ? names?.GetValueOrDefault(updated) : null, creatorOnlyDelete: creatorOnlyWorkDelete)).ToList(),
        flight.TaskRecords.Where(t => t.ReturnToRampId == occurrence.Id).OrderBy(t => t.CreatedAtUtc).ThenBy(t => t.Id)
            .Select(t => FlightTaskDto.Map(t, userId, canOperate, canOperate, names?.GetValueOrDefault(t.CreatedByUserId),
                t.EndedByUserId is { } closer ? names?.GetValueOrDefault(closer) : null,
                t.UpdatedByUserId is { } updated ? names?.GetValueOrDefault(updated) : null, creatorOnlyDelete: creatorOnlyWorkDelete)).ToList());
}
