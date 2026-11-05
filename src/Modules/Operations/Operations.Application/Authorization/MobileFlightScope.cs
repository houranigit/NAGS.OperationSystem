using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Domain.Results;

namespace Operations.Application.Authorization;

public sealed record MobileFlightScopeContext(
    Guid StationId,
    Guid StaffMemberId,
    Guid UserId,
    DateTimeOffset AsOfUtc)
{
    public DateTimeOffset WindowStartUtc => AsOfUtc.AddHours(-12);
    public DateTimeOffset WindowEndUtc => AsOfUtc.AddHours(12);
}

public interface IMobileFlightScope
{
    public Task<Result<MobileFlightScopeContext>> ResolveAsync(CancellationToken cancellationToken);
}

/// <summary>Resolves the mobile station/staff boundary without widening it for portal readers or dispatchers.</summary>
public sealed class MobileFlightScope(IOperationsScope scope, IUserContext user, TimeProvider timeProvider)
    : IMobileFlightScope
{
    public async Task<Result<MobileFlightScopeContext>> ResolveAsync(CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        if (resolved.Value is not { StationId: { } stationId, StaffMemberId: { } staffMemberId })
            return Error.Forbidden(
                "The mobile app requires an account linked to an active staff member.",
                "Operations.Mobile.StaffLinkRequired");

        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.Scope.Unauthenticated");

        return new MobileFlightScopeContext(stationId, staffMemberId, userId, timeProvider.GetUtcNow());
    }
}
