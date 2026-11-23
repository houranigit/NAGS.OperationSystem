using Identity.Contracts;
using Operations.Domain.Flights;

namespace Operations.Application.Common;

internal static class FlightWorkAttribution
{
    internal static IEnumerable<Guid?> UserIds(Flight flight) =>
        flight.ArrivalSubmissions.SelectMany(item => new Guid?[] { item.CreatedByUserId, item.UpdatedByUserId })
            .Concat(flight.DepartureSubmissions.SelectMany(item => new Guid?[] { item.CreatedByUserId, item.UpdatedByUserId }))
            .Concat(UnlinkedCancellationUserIds(flight))
            .Concat(flight.ServiceRecords.SelectMany(item => new Guid?[] { item.CreatedByUserId, item.EndedByUserId, item.UpdatedByUserId }))
            .Concat(flight.TaskRecords.SelectMany(item => new Guid?[] { item.CreatedByUserId, item.EndedByUserId, item.UpdatedByUserId })).Concat(flight.ReturnToRamps.SelectMany(item => new Guid?[] { item.CreatedByUserId, item.EndedByUserId, item.UpdatedByUserId })).Append(flight.CreatedByUserId);

    internal static IEnumerable<Guid?> UnlinkedCancellationUserIds(Flight flight) =>
        flight.CancellationSubmissions.Where(item => item.SubmittedByEmployee is null).Select(item => (Guid?)item.CreatedByUserId);

    internal static Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IUserDisplayNameReader? reader,
        IEnumerable<Guid?> userIds, CancellationToken cancellationToken) => reader is null
        ? Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>())
        : reader.GetDisplayNamesAsync(userIds.Where(id => id.HasValue && id != Guid.Empty)
            .Select(id => id!.Value).Distinct().ToArray(), cancellationToken);
}
