using Identity.Contracts;

namespace Operations.Application.UnitTests;

internal sealed class WorkUserNames(Dictionary<Guid, string>? names = null) : IUserDisplayNameReader
{
    public Dictionary<Guid, string> Values { get; } = names ?? [];
    public List<Guid[]> Requests { get; } = [];
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(userIds.ToArray());
        return Task.FromResult<IReadOnlyDictionary<Guid, string>>(Values.Where(item => userIds.Contains(item.Key))
            .ToDictionary(item => item.Key, item => item.Value));
    }
}
