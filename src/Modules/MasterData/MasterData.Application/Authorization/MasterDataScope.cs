using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Authorization;

/// <summary>
/// The data boundary the current caller may act within, resolved server-side. A
/// <see cref="UserType.SystemAdministrator"/> is unrestricted; a <see cref="UserType.StationStaff"/>
/// is confined to <see cref="StationId"/>; a <see cref="UserType.CustomerContact"/> is confined to
/// <see cref="CustomerId"/>. Scoped accounts always carry exactly one boundary id.
/// </summary>
public sealed record MasterDataScopeContext(UserType UserType, Guid? StationId, Guid? CustomerId)
{
    public bool IsAdministrator => UserType == UserType.SystemAdministrator;

    /// <summary>Denies access to a Station outside the caller's boundary. Administrators pass.</summary>
    public Result EnsureStation(Guid stationId) =>
        IsAdministrator || StationId == stationId
            ? Result.Success()
            : Forbidden();

    /// <summary>Denies access to a Customer outside the caller's boundary. Administrators pass.</summary>
    public Result EnsureCustomer(Guid customerId) =>
        IsAdministrator || CustomerId == customerId
            ? Result.Success()
            : Forbidden();

    private static Error Forbidden() =>
        Error.Forbidden("This record is outside your data scope.", "MasterData.Scope.Forbidden");
}

/// <summary>
/// Resolves and enforces the caller's MasterData data scope. Resolution fails closed: a scoped
/// account is denied whenever its linked record or the parent Station/Customer is missing or
/// inactive, even while a provisioning/deactivation integration event is still in flight.
/// </summary>
public interface IMasterDataScope
{
    public Task<Result<MasterDataScopeContext>> ResolveAsync(CancellationToken cancellationToken);
}

public sealed class MasterDataScope(IUserContext user, IMasterDataDbContext db) : IMasterDataScope
{
    public async Task<Result<MasterDataScopeContext>> ResolveAsync(CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated || user.UserType is not { } userType)
            return Error.Forbidden("The request is not authenticated.", "MasterData.Scope.Unauthenticated");

        switch (userType)
        {
            case UserType.SystemAdministrator:
                return new MasterDataScopeContext(userType, null, null);

            case UserType.StationStaff:
            {
                if (user.ExternalReferenceId is not { } staffId)
                    return Denied();

                var staff = await db.StaffMembers.AsNoTracking()
                    .Where(s => s.Id == staffId && s.IsActive)
                    .Select(s => new { s.StationId })
                    .FirstOrDefaultAsync(cancellationToken);
                if (staff is null)
                    return Denied();

                var stationActive = await db.Stations.AsNoTracking()
                    .AnyAsync(s => s.Id == staff.StationId && s.IsActive, cancellationToken);
                if (!stationActive)
                    return Denied();

                return new MasterDataScopeContext(userType, staff.StationId, null);
            }

            case UserType.CustomerContact:
            {
                if (user.ExternalReferenceId is not { } contactId)
                    return Denied();

                var match = await db.Customers.AsNoTracking()
                    .Where(c => c.IsActive && c.Contacts.Any(ct => ct.Id == contactId && ct.IsActive))
                    .Select(c => new { c.Id })
                    .FirstOrDefaultAsync(cancellationToken);
                if (match is null)
                    return Denied();

                return new MasterDataScopeContext(userType, null, match.Id);
            }

            default:
                return Denied();
        }
    }

    private static Error Denied() =>
        Error.Forbidden("Your linked record or its parent is missing or inactive.", "MasterData.Scope.Denied");
}

public static class MasterDataScopeExtensions
{
    /// <summary>Resolves the caller's scope and denies access to a Station outside it.</summary>
    public static async Task<Result> CheckStationAsync(this IMasterDataScope scope, Guid stationId, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        return resolved.IsFailure ? resolved.Error : resolved.Value.EnsureStation(stationId);
    }

    /// <summary>Resolves the caller's scope and denies access to a Customer outside it.</summary>
    public static async Task<Result> CheckCustomerAsync(this IMasterDataScope scope, Guid customerId, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        return resolved.IsFailure ? resolved.Error : resolved.Value.EnsureCustomer(customerId);
    }
}
