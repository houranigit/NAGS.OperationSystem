using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public partial class PortalFlightWorkDialog
{
    private Guid? unlockedArrivalId;
    private Guid? unlockedDepartureId;

    private bool ArrivalFieldsLocked => arrivalSource is { } arrival && unlockedArrivalId != arrival.Id;
    private bool DepartureFieldsLocked => departureSource is { } departure && unlockedDepartureId != departure.Id;
    private bool CanEditArrivalFields => CanEditArrival && !ArrivalFieldsLocked && !saving;

    private static bool IsStaffSubmitter(PortalStaffSnapshot? employee, Guid? staffId) =>
        staffId.HasValue && employee?.StaffMemberId == staffId;

    private string ActualsSubmitter(PortalStaffSnapshot? employee, string? creator)
    {
        if (IsStaffSubmitter(employee, flight?.CurrentStaffMemberId))
            return "You";

        return string.IsNullOrWhiteSpace(creator) ? employee?.FullName ?? "Unknown submitter" : creator;
    }

    private string ActualsTimestamp(DateTimeOffset stamp) => zone.Format(stamp, "MMM d, yyyy 'at' HH:mm");
}
