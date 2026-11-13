using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

/// <summary>Unassigned regular flights have first-save or immutable closure grants, never virtual assignment.</summary>
internal static class FlightSearchPolicy
{
    internal static bool IsUnassignedRegular(Flight flight, Guid staffId) => !flight.IsPerLanding &&
        flight.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType &&
        !flight.AssignedEmployees.Any(item => item.Employee.StaffMemberId == staffId);

    internal static bool CanAddWork(Flight flight, Guid staffId) => !IsUnassignedRegular(flight, staffId) || !flight.HasOperationalRecords();
    internal static Result NewWork(Flight flight, Guid staffId) => CanAddWork(flight, staffId) ? Result.Success() :
        Error.Forbidden("Only existing open work can be completed on this unassigned flight.", "Operations.Search.NewWorkReadOnly");

    internal static Result Arrival(Flight flight, Guid staffId, FlightArrivalSubmission? existing) =>
        !IsUnassignedRegular(flight, staffId) || existing?.SubmittedByEmployee.StaffMemberId == staffId || !flight.HasOperationalRecords()
            ? Result.Success() : Error.Forbidden("Arrival details are read-only on this unassigned flight.", "Operations.Search.ArrivalReadOnly");

    internal static bool ServiceClosure(FlightService record, SaveFlightServiceRequest input, bool mediaMatches) =>
        record.Window.ToUtc is null && input.EndUtc.HasValue && mediaMatches && record.Service.ServiceId == input.ServiceId &&
        record.Window.FromUtc == input.StartUtc && SameIdentifier(record.CustomerReferenceNumber, input.CustomerReferenceNumber) &&
        record.Notes == Normalize(input.Notes) && record.Performers.Count == input.Performers.Count &&
        record.Performers.All(old => input.Performers.Any(item => item.Id == old.Id && item.StaffMemberId == old.Employee.StaffMemberId &&
            item.StartUtc == old.Window.FromUtc && item.EndUtc.HasValue && (!old.Window.ToUtc.HasValue || item.EndUtc == old.Window.ToUtc)));

    internal static bool TaskClosure(FlightTask record, SaveFlightTaskRequest input, bool mediaMatches) =>
        record.Window.ToUtc is null && input.EndUtc.HasValue && mediaMatches && (int)record.TaskType == input.TaskType &&
        record.AtaChapter?.AtaChapterId == input.AtaChapterId && record.Window.FromUtc == input.StartUtc &&
        SameIdentifier(record.CustomerReferenceNumber, input.CustomerReferenceNumber) && record.Notes == Normalize(input.Notes) &&
        record.Performers.Count == input.Performers.Count && record.Performers.All(old => input.Performers.Any(item =>
            item.Id == old.Id && item.StaffMemberId == old.Employee.StaffMemberId && item.StartUtc == old.Window.FromUtc &&
            Normalize(item.Notes) == old.Notes && item.EndUtc.HasValue && (!old.Window.ToUtc.HasValue || item.EndUtc == old.Window.ToUtc))) &&
        record.Tools.Count == input.Tools.Count && record.Tools.All(old => input.Tools.Any(item => Resource(old.Id, old.Tool.ToolId, old.Usage, old.Notes, item))) &&
        record.Materials.Count == input.Materials.Count && record.Materials.All(old => input.Materials.Any(item => Resource(old.Id, old.Material.MaterialId, old.Usage, old.Notes, item))) &&
        record.GeneralSupports.Count == input.GeneralSupports.Count && record.GeneralSupports.All(old => input.GeneralSupports.Any(item => Resource(old.Id, old.GeneralSupport.GeneralSupportId, old.Usage, old.Notes, item)));

    internal static bool BypassServiceAllowance(Flight flight, MobileFlightScopeContext actor, FlightService? existing,
        SaveFlightServiceRequest input, bool matches, bool mediaMatches) => IsUnassignedRegular(flight, actor.StaffMemberId) && existing is not null &&
        (matches && existing.EndedByUserId == actor.UserId || ServiceClosure(existing, input, mediaMatches));

    internal static Error ClosureReadOnly() => Error.Forbidden(
        "Only the parent end and missing employee or duration resource ends can change when completing another employee's open work.",
        "Operations.Search.ClosureReadOnly");

    private static bool Resource(Guid id, Guid referenceId, Operations.Domain.ValueObjects.ResourceUsage usage, string? notes, SaveFlightTaskResourceRequest input) =>
        id == input.Id && referenceId == input.ResourceId && usage.Quantity == input.Quantity && usage.FromUtc == input.StartUtc && notes == Normalize(input.Notes) &&
        (usage.Quantity.HasValue ? input.EndUtc == usage.ToUtc : input.EndUtc.HasValue && (!usage.ToUtc.HasValue || input.EndUtc == usage.ToUtc));
    private static bool SameIdentifier(string? stored, string? input) => Normalize(stored)?.ToUpperInvariant() == Normalize(input)?.ToUpperInvariant();
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
