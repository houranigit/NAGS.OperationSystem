namespace Identity.Contracts;

/// <summary>Resolves only the requested accounts' current display names, including retained inactive accounts.</summary>
public interface IUserDisplayNameReader
{
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);
}
