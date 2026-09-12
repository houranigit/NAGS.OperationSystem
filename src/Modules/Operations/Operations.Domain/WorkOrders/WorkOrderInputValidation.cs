using BuildingBlocks.Domain.Results;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.WorkOrders;

public static class WorkOrderInputValidation
{
    public static Result EmployeeAssignments(
        IEnumerable<Guid> selectedStaffIds,
        IReadOnlyList<WorkOrderEmployeeAssignmentInput>? assignments,
        TimeWindow parentWindow)
    {
        // Older installed clients and persisted outbox requests represent full-line participation.
        // New clients explicitly send one closed period for every selected employee.
        if (assignments is null)
            return Result.Success();

        var selected = selectedStaffIds.ToHashSet();
        if (assignments.Any(item => item.StaffMemberId == Guid.Empty) ||
            assignments.Select(item => item.StaffMemberId).Distinct().Count() != assignments.Count ||
            !selected.SetEquals(assignments.Select(item => item.StaffMemberId)))
            return Error.Validation("Provide exactly one working period for every selected employee.", "Operations.WorkOrder.EmployeeAssignmentsRequired");

        foreach (var assignment in assignments)
        {
            if (assignment.Window is null)
                return Error.Validation("Every employee needs From and To times.", "Operations.WorkOrder.EmployeeWindowRequired");
            if (assignment.Window.From < parentWindow.From)
                return Error.Validation("Employee From time cannot be before the service or task From time.", "Operations.WorkOrder.EmployeeFromBeforeLine");
            if (assignment.Window.To > parentWindow.To)
                return Error.Validation("Employee To time cannot be after the service or task To time.", "Operations.WorkOrder.EmployeeToAfterLine");
        }

        return Result.Success();
    }

    public static Result Description(Guid selectedId, Guid unknownId, string? description, string label)
    {
        if (selectedId == unknownId && string.IsNullOrWhiteSpace(description))
            return Error.Validation($"A description is required when Unknown {label} is selected.", "Operations.WorkOrder.UnknownDescriptionRequired");
        if (description?.Trim().Length > 2000)
            return Error.Validation($"{label} description must be at most 2000 characters.", "Operations.WorkOrder.DescriptionTooLong");
        return Result.Success();
    }
}
