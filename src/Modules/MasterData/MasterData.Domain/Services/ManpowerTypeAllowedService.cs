namespace MasterData.Domain.Services;

/// <summary>
/// Explicit allowance granting a manpower type permission to record a service as performed work.
/// Planned-flight services are intentionally independent of this relationship.
/// </summary>
public sealed class ManpowerTypeAllowedService
{
    private ManpowerTypeAllowedService() { }

    public Guid ManpowerTypeId { get; private set; }
    public Guid ServiceId { get; private set; }

    public static ManpowerTypeAllowedService Create(Guid manpowerTypeId, Guid serviceId) => new()
    {
        ManpowerTypeId = manpowerTypeId,
        ServiceId = serviceId
    };
}
