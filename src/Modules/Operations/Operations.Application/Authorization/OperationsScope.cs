using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;

namespace Operations.Application.Authorization;

/// <summary>
/// The data boundary the current caller may act within. A <see cref="UserType.SystemAdministrator"/>
/// is unrestricted; a <see cref="UserType.StationStaff"/> is confined to <see cref="StationId"/>.
/// CustomerContacts have no Operations access in this release.
/// </summary>
public sealed record OperationsScopeContext(UserType UserType, Guid? StationId, Guid? StaffMemberId)
{
    public bool IsAdministrator => UserType == UserType.SystemAdministrator;

    public Result EnsureStation(Guid stationId) =>
        IsAdministrator || StationId == stationId
            ? Result.Success()
            : Error.Forbidden("This flight is outside your station scope.", "Operations.Scope.Forbidden");
}

/// <summary>
/// Resolves and enforces the caller's Operations data scope. Resolution fails closed: a station-staff
/// account is denied whenever its linked StaffMember or its Station is missing or inactive.
/// </summary>
public interface IOperationsScope
{
    public Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken);
}

public sealed class OperationsScope(IUserContext user, IMasterDataReader masterData) : IOperationsScope
{
    public async Task<Result<OperationsScopeContext>> ResolveAsync(CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated || user.UserType is not { } userType)
            return Error.Forbidden("The request is not authenticated.", "Operations.Scope.Unauthenticated");

        switch (userType)
        {
            case UserType.SystemAdministrator:
                return new OperationsScopeContext(userType, null, null);

            case UserType.StationStaff:
            {
                if (user.ExternalReferenceId is not { } staffId)
                    return Denied();

                var staff = await masterData.GetStaffMemberAsync(staffId, cancellationToken);
                if (staff is null || !staff.IsActive)
                    return Denied();

                var station = await masterData.GetStationAsync(staff.StationId, cancellationToken);
                if (station is null || !station.IsActive)
                    return Denied();

                return new OperationsScopeContext(userType, staff.StationId, staffId);
            }

            default:
                return Denied();
        }
    }

    private static Error Denied() =>
        Error.Forbidden("Your linked staff record or its station is missing or inactive.", "Operations.Scope.Denied");
}

public static class OperationsScopeExtensions
{
    public static async Task<Result<OperationsScopeContext>> ResolveForWriteAsync(this IOperationsScope scope, CancellationToken cancellationToken) =>
        await scope.ResolveAsync(cancellationToken);
}
