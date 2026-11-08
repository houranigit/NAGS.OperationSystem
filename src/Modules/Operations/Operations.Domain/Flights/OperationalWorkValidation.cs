using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Resources;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

internal static class OperationalWorkValidation
{
    internal static Result ReferenceNumber(string? value) =>
        value?.Trim().Length > 200
            ? Error.Validation("Customer reference number must be at most 200 characters.", "Operations.WorkOrder.CustomerReferenceNumberTooLong")
            : Result.Success();

    internal static Result Notes(string? value) =>
        value?.Trim().Length > 2000
            ? Error.Validation("Description must be at most 2000 characters.", "Operations.WorkOrder.DescriptionTooLong")
            : Result.Success();

    internal static Result Description(Guid selectedId, Guid unknownId, string? notes, string label)
    {
        if (selectedId == unknownId && string.IsNullOrWhiteSpace(notes))
            return Error.Validation($"A description is required when Unknown {label} is selected.", "Operations.WorkOrder.UnknownDescriptionRequired");
        return Notes(notes);
    }

    internal static Result EmployeeWindow(TimeWindow child, TimeWindow parent)
    {
        if (child.FromUtc < parent.FromUtc)
            return Error.Validation("Employee From time cannot be before the service or task From time.", "Operations.WorkOrder.EmployeeFromBeforeLine");
        if (parent.ToUtc.HasValue && child.FromUtc > parent.ToUtc)
            return Error.Validation("Employee From time cannot be after the service or task To time.", "Operations.WorkOrder.EmployeeFromAfterLine");
        if (parent.ToUtc.HasValue && child.ToUtc > parent.ToUtc)
            return Error.Validation("Employee To time cannot be after the service or task To time.", "Operations.WorkOrder.EmployeeToAfterLine");
        return Result.Success();
    }

    internal static Result ResourceWindow(ResourceUsage usage, TimeWindow parent, string kind)
    {
        if (usage.CalculationType != ResourceCalculationType.Duration)
            return Result.Success();
        if (usage.FromUtc < parent.FromUtc)
            return Error.Validation("Resource usage From time cannot be before its task From time.", $"Operations.ResourceUsage.{kind}FromBeforeTask");
        if (parent.ToUtc.HasValue && usage.ToUtc > parent.ToUtc)
            return Error.Validation("Resource usage To time cannot be after its task To time.", $"Operations.ResourceUsage.{kind}ToAfterTask");
        return Result.Success();
    }

    internal static Error DuplicateEmployee() =>
        Error.Validation("Provide exactly one working period for every selected employee.", "Operations.WorkOrder.EmployeeAssignmentsRequired");
}
