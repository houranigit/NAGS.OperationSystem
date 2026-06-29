using BuildingBlocks.Api.Modules;
using MasterData.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace MasterData.Api;

/// <summary>Maps all MasterData endpoints under <c>/api/v1/masterdata</c>.</summary>
public sealed class MasterDataEndpointModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/masterdata");

        CountryEndpoints.Map(group);
        ManpowerTypeEndpoints.Map(group);
        LicenseEndpoints.Map(group);
        ServiceEndpoints.Map(group);
        OperationTypeEndpoints.Map(group);
        AircraftTypeEndpoints.Map(group);
        ToolEndpoints.Map(group);
        MaterialEndpoints.Map(group);
        GeneralSupportEndpoints.Map(group);
        StationEndpoints.Map(group);
        CustomerEndpoints.Map(group);
        StaffMemberEndpoints.Map(group);
    }
}
