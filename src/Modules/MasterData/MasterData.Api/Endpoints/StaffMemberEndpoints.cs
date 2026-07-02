using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MasterData.Application.Features.PortalAccess;
using MasterData.Application.Features.StaffMembers;
using MasterData.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MasterData.Api.Endpoints;

internal static class StaffMemberEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var staff = group.MapGroup("/staff-members").WithTags("MasterData.StaffMembers");

        staff.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, bool? isActive = null,
            Guid? stationId = null, Guid? manpowerTypeId = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetStaffMembersQuery(page, pageSize, search, isActive, stationId, manpowerTypeId, sort), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.StaffMembers.View);

        staff.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetStaffMemberByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.StaffMembers.View);

        staff.MapPost("/", async (CreateStaffMemberRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateStaffMemberCommand(
                request.FullName, request.EmployeeId, request.Email, request.StationId, request.ManpowerTypeId,
                MapContract(request.EmploymentContract), request.WorkingDays, MapLicenses(request.Licenses), request.PortalAccessRoleId), ct);
            return result.ToCreated(id => $"/api/v1/masterdata/staff-members/{id}");
        }).RequirePermission(MasterDataPermissions.StaffMembers.Create);

        staff.MapPut("/{id:guid}", async (Guid id, UpdateStaffMemberRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateStaffMemberCommand(
                id, request.FullName, request.EmployeeId, request.Email, request.StationId, request.ManpowerTypeId,
                MapContract(request.EmploymentContract), request.WorkingDays, MapLicenses(request.Licenses), rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.StaffMembers.Update);

        staff.MapPost("/{id:guid}/activate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ActivateStaffMemberCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.StaffMembers.Activate);

        staff.MapPost("/{id:guid}/deactivate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeactivateStaffMemberCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.StaffMembers.Deactivate);

        staff.MapPost("/{id:guid}/grant-access", async (Guid id, GrantPortalAccessRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new GrantStaffPortalAccessCommand(id, request.RoleId, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.StaffMembers.GrantAccess);
    }

    private static EmploymentContractInput? MapContract(EmploymentContractRequest? request) =>
        request is null ? null : new EmploymentContractInput(request.StartDate, request.EndDate);

    private static IReadOnlyList<StaffLicenseInput> MapLicenses(IReadOnlyList<StaffLicenseRequest>? licenses) =>
        licenses is null
            ? []
            : licenses.Select(l => new StaffLicenseInput(l.Id, l.LicenseId, l.LicenseNumber)).ToList();
}
