using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public sealed record PortalReturnToRampSummary(
    Guid Id,
    int Sequence,
    DateTimeOffset ArrivedAtUtc,
    DateTimeOffset? DepartedAtUtc,
    bool IsDraft,
    bool HasSignature,
    string? ReceivedBy,
    string? HandledBy,
    string? ArrivalRemarks,
    string? DepartureRemarks)
{
    public static PortalReturnToRampSummary From(PortalFlightReturnToRamp occurrence) =>
        new(
            occurrence.Id,
            occurrence.Sequence,
            occurrence.ArrivedAtUtc,
            occurrence.DepartedAtUtc,
            IsDraft: false,
            occurrence.HasSignature,
            occurrence.ReceivedByFullName,
            occurrence.HandledByFullName,
            occurrence.ArrivalRemarks,
            occurrence.DepartureRemarks);

    public static PortalReturnToRampSummary From(
        PortalReturnToRampDraft draft, IReadOnlyList<PortalCatalogChoice> staff, Guid? currentStaffId)
    {
        var request = draft.Request;
        var departure = request.Departure;

        return new(
            draft.Id,
            draft.Sequence,
            request.Arrival.ArrivedAtUtc,
            departure?.DepartedAtUtc,
            IsDraft: true,
            departure?.SignatureContent is not null,
            StaffName(request.Arrival.ReceivedByStaffMemberId ?? currentStaffId),
            departure is null ? null : StaffName(departure.HandledByStaffMemberId ?? currentStaffId),
            request.Arrival.Remarks,
            departure?.Remarks);

        string? StaffName(Guid? id)
        {
            var member = staff.FirstOrDefault(candidate => candidate.Id == id);
            return member?.FullName ?? member?.Label;
        }
    }
}
