using BuildingBlocks.Domain.Results;
using Operations.Application.Contracts;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

internal static class PortalFlightRecordCorrections
{
    internal static Error SavedDetailsRemovalForbidden() => Error.Forbidden(
        "System administrators cannot remove saved details from a flight operation record.",
        "Operations.Portal.SavedDetailsRemovalForbidden");

    internal static bool RetainsSavedDetails(FlightService saved, SaveFlightServiceRequest input) =>
        RetainsIds(saved.Performers.Select(item => item.Id), input.Performers.Select(item => item.Id)) &&
        RetainsIds(saved.Attachments.Select(item => item.Id), input.Attachments?.Select(item => item.Id));

    internal static bool RetainsSavedDetails(FlightTask saved, SaveFlightTaskRequest input) =>
        RetainsIds(saved.Performers.Select(item => item.Id), input.Performers.Select(item => item.Id)) &&
        RetainsIds(saved.Tools.Select(item => item.Id), input.Tools.Select(item => item.Id)) &&
        RetainsIds(saved.Materials.Select(item => item.Id), input.Materials.Select(item => item.Id)) &&
        RetainsIds(saved.GeneralSupports.Select(item => item.Id), input.GeneralSupports.Select(item => item.Id)) &&
        RetainsIds(saved.Attachments.Select(item => item.Id), input.Attachments?.Select(item => item.Id));

    // Omitted attachments retain the saved list. Supplied lists can add IDs but must retain all saved IDs.
    private static bool RetainsIds(IEnumerable<Guid> saved, IEnumerable<Guid>? input) =>
        input is null || input.ToHashSet().IsSupersetOf(saved);
}
