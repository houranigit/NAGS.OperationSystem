using BuildingBlocks.Contracts.Authorization;

namespace BuildingBlocks.Application.Abstractions;

/// <summary>
/// The authenticated caller's business identity for the current request, available to any module.
/// Modules use it to enforce server-side data scope (see the data-scope rules in the MasterData
/// foundation): a <see cref="UserType.StationStaff"/>/<see cref="UserType.CustomerContact"/> account
/// is restricted to the MasterData record identified by <see cref="ExternalReferenceId"/>.
/// </summary>
public interface IUserContext
{
    public bool IsAuthenticated { get; }

    public Guid? UserId { get; }

    /// <summary>The caller's user type, or null when unauthenticated or the claim is absent.</summary>
    public UserType? UserType { get; }

    /// <summary>The linked MasterData record id for linked accounts; null for direct account types.</summary>
    public Guid? ExternalReferenceId { get; }

    /// <summary>
    /// True when the caller holds <paramref name="permission"/>. Lets application handlers enforce
    /// permission-gated sub-operations (e.g. supplying portal access during a create) as
    /// defense-in-depth alongside the endpoint policy.
    /// </summary>
    public bool HasPermission(string permission);
}
