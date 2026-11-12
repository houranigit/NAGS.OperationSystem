using Identity.Contracts;

namespace Operations.Application.Common;

internal static class FlightWorkAttribution
{
    internal static Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IUserDisplayNameReader? reader,
        IEnumerable<Guid?> userIds, CancellationToken cancellationToken) => reader is null
        ? Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>())
        : reader.GetDisplayNamesAsync(userIds.Where(id => id.HasValue && id != Guid.Empty)
            .Select(id => id!.Value).Distinct().ToArray(), cancellationToken);
}
