using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using Operations.Application.Authorization;
using Operations.Domain.Authorization;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

public sealed class PortalFlightScope(IOperationsScope scope, IUserContext user, TimeProvider clock)
{
    internal static bool CanReview(IUserContext? caller) =>
        caller?.UserType == UserType.SystemAdministrator ||
        caller?.UserType == UserType.StationStaff && caller.HasPermission(OperationsPermissions.Flights.Approve);

    internal Task<Result<FlightOperationAccess>> ResolveApprovalAsync(CancellationToken cancellationToken) =>
        CanReview(user)
            ? ResolveReadAsync(cancellationToken)
            : Task.FromResult<Result<FlightOperationAccess>>(Error.Forbidden(
                "Flight approval permission is required.", "Operations.Flight.ApprovalForbidden"));

    internal async Task<Result<FlightOperationAccess>> ResolveReadAsync(CancellationToken cancellationToken)
    {
        if (!CanReview(user))
            return await ResolveAsync(cancellationToken);

        if (!user.HasPermission(OperationsPermissions.Flights.View) || user.UserId is not { } userId)
            return Denied();

        var resolved = await scope.ResolveForWriteAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        var portal = resolved.Value;
        if (portal.IsAdministrator)
            return new FlightOperationAccess(null, portal, userId);

        if (portal is not { StationId: { } stationId, StaffMemberId: { } staffId })
            return Denied();

        return new FlightOperationAccess(new MobileFlightScopeContext(stationId, staffId, userId, clock.GetUtcNow()), portal);
    }

    internal Task<Result<FlightOperationAccess>> ResolveCorrectionAsync(CancellationToken cancellationToken) =>
        user.UserType == UserType.SystemAdministrator && user.HasPermission(OperationsPermissions.Flights.Operate)
            ? ResolveReadAsync(cancellationToken)
            : ResolveAsync(cancellationToken);

    internal async Task<Result<FlightOperationAccess>> ResolveAsync(CancellationToken cancellationToken)
    {
        if (!user.HasPermission(OperationsPermissions.Flights.View) || !user.HasPermission(OperationsPermissions.Flights.Operate))
            return Denied();

        var resolved = await scope.ResolveForWriteAsync(cancellationToken);
        if (resolved.IsFailure) return resolved.Error;

        if (resolved.Value is not { StationId: { } stationId, StaffMemberId: { } staffId } || user.UserId is not { } userId)
            return Denied();

        return new FlightOperationAccess(new MobileFlightScopeContext(stationId, staffId, userId, clock.GetUtcNow()), resolved.Value);
    }

    internal static Error Denied() => Error.Forbidden(
        "Flight operations requires an active station-linked staff account with flight view and operate permissions.",
        "Operations.Portal.FlightOperationsForbidden");
}

internal sealed record FlightOperationAccess(MobileFlightScopeContext? StaffActor, OperationsScopeContext? PortalScope = null,
    Guid? AdministratorUserId = null)
{
    internal bool IsAdministratorView => PortalScope?.IsAdministrator == true;
    internal MobileFlightScopeContext Actor => StaffActor ?? throw new InvalidOperationException("An employee is required for flight operations.");
    internal Guid UserId => AdministratorUserId ?? Actor.UserId;

    internal IQueryable<Flight> Apply(IQueryable<Flight> flights) => PortalScope is { } portal
        ? FlightListQuery.ApplyPortalVisibility(flights, portal)
        : MobileFlightQuery.ApplyReadScope(flights, Actor);

    internal static Task<Result<FlightOperationAccess>> ResolveReadAsync(IMobileFlightScope mobileScope,
        PortalFlightScope? portalScope, bool portalOnly, CancellationToken cancellationToken) =>
        portalOnly && portalScope is not null
            ? portalScope.ResolveReadAsync(cancellationToken)
            : ResolveAsync(mobileScope, portalScope, portalOnly, cancellationToken);

    internal static Task<Result<FlightOperationAccess>> ResolveCorrectionAsync(IMobileFlightScope mobileScope,
        PortalFlightScope? portalScope, bool portalOnly, CancellationToken cancellationToken) =>
        portalOnly && portalScope is not null
            ? portalScope.ResolveCorrectionAsync(cancellationToken)
            : ResolveAsync(mobileScope, portalScope, portalOnly, cancellationToken);

    internal static async Task<Result<FlightOperationAccess>> ResolveAsync(IMobileFlightScope mobileScope,
        PortalFlightScope? portalScope, bool portalOnly, CancellationToken cancellationToken)
    {
        if (portalOnly)
            return portalScope is null ? PortalFlightScope.Denied() : await portalScope.ResolveAsync(cancellationToken);

        var resolved = await mobileScope.ResolveAsync(cancellationToken);
        return resolved.IsFailure ? resolved.Error : new FlightOperationAccess(resolved.Value);
    }
}
