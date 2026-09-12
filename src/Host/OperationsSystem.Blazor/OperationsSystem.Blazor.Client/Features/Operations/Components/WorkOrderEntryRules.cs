using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public sealed record EmployeeAssignmentDraft(Guid StaffMemberId, DateTime? FromLocal = null, DateTime? ToLocal = null);

internal static class WorkOrderEntryRules
{
    internal static readonly Guid UnknownCustomer = new("50000000-0000-0000-0000-000000000001");
    internal static readonly Guid UnknownService = new("40000000-0000-0000-0000-000000000003");
    private static readonly HashSet<Guid> UnknownResources =
    [
        new("60000000-0000-0000-0000-000000000001"),
        new("70000000-0000-0000-0000-000000000001"),
        new("80000000-0000-0000-0000-000000000001")
    ];

    internal static bool IsUnknownResource(Guid? id) => id.HasValue && UnknownResources.Contains(id.Value);
    internal static bool RequiresCustomerDescription(Guid? customerId) => customerId == UnknownCustomer;
    internal static bool RequiresServiceDescription(Guid? serviceId) => serviceId == UnknownService;

    internal static IReadOnlyList<EmployeeAssignmentDraft> SynchronizeAssignments(
        IEnumerable<Guid>? selectedIds,
        IReadOnlyList<EmployeeAssignmentDraft> assignments) =>
        (selectedIds ?? []).Distinct().Select(id => assignments.FirstOrDefault(item => item.StaffMemberId == id)
            ?? new EmployeeAssignmentDraft(id)).ToList();

    internal static List<string> ValidateAssignments(
        IEnumerable<Guid>? selectedIds,
        IReadOnlyList<EmployeeAssignmentDraft> assignments,
        DateTime? fromLocal,
        DateTime? toLocal,
        UserTimeZone timeZone,
        string label)
    {
        var messages = new List<string>();
        foreach (var id in (selectedIds ?? []).Distinct())
        {
            var assignment = assignments.SingleOrDefault(item => item.StaffMemberId == id);
            if (assignment?.FromLocal is not { } from || assignment.ToLocal is not { } to)
            {
                messages.Add($"{label}: enter From and To times for every selected employee.");
                continue;
            }
            if (to < from)
                messages.Add($"{label}: employee To time cannot be before From time.");
            if (fromLocal.HasValue && from < fromLocal.Value)
                messages.Add($"{label}: employee From time cannot be before the service/task From time.");
            if (toLocal.HasValue && to > toLocal.Value)
                messages.Add($"{label}: employee To time cannot be after the service/task To time.");
            if (!timeZone.TryToUtc(from, out _, out var fromError))
                messages.Add($"{label} employee From: {fromError}");
            if (!timeZone.TryToUtc(to, out _, out var toError))
                messages.Add($"{label} employee To: {toError}");
        }
        return messages.Distinct().ToList();
    }

    internal static IReadOnlyList<WorkOrderEmployeeAssignmentRequestModel> ToRequests(
        IReadOnlyList<EmployeeAssignmentDraft> assignments, UserTimeZone timeZone) =>
        assignments.Select(item => new WorkOrderEmployeeAssignmentRequestModel(
            item.StaffMemberId, timeZone.ToUtc(item.FromLocal)!.Value, timeZone.ToUtc(item.ToLocal)!.Value)).ToList();
}
