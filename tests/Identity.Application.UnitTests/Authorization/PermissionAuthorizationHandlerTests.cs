using System.Security.Claims;
using Audit.Application.Authorization;
using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Application.Authorization;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Authorization;
using MasterData.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Operations.Application.Authorization;
using Operations.Domain.Authorization;
using Shouldly;

namespace Identity.Application.UnitTests.Authorization;

public sealed class PermissionAuthorizationHandlerTests
{
    [Fact]
    public async Task Viewer_only_with_forged_incompatible_mutation_claim_is_denied()
    {
        var permission = OperationsPermissions.Flights.Schedule;
        var context = ContextForViewer(permission);

        await Handler().HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Viewer_only_with_compatible_view_claim_is_authorized()
    {
        var permission = OperationsPermissions.Dashboard.View;
        var context = ContextForViewer(permission);

        await Handler().HandleAsync(context);

        context.HasSucceeded.ShouldBeTrue();
    }

    private static PermissionAuthorizationHandler Handler() =>
        new(new PermissionRegistry(
        [
            new IdentityPermissionCatalog(),
            new MasterDataPermissionCatalog(),
            new OperationsPermissionCatalog(),
            new AuditPermissionCatalog()
        ]));

    private static AuthorizationHandlerContext ContextForViewer(string permission)
    {
        var requirement = new PermissionRequirement(permission);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AuthorizationClaimTypes.UserType, UserType.ViewerOnly.ToString()),
            new Claim(PermissionPolicy.ClaimType, permission)
        ],
            authenticationType: "test"));

        return new AuthorizationHandlerContext([requirement], principal, resource: null);
    }
}
