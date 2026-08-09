using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Operations.Api;
using Operations.Application.Features.WorkOrders;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class WorkOrderEndpointUploadLimitTests
{
    [Fact]
    public void Json_work_order_routes_share_the_inline_file_request_limit()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(DispatchProxy.Create<ISender, ThrowingProxy>());
        builder.Services.AddSingleton(TimeProvider.System);
        using var app = builder.Build();
        new OperationsEndpointModule().MapEndpoints(app);

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
        var expectedRoutes = new (string Method, string Route)[]
        {
            ("POST", "/api/v1/operations/flights/{flightId:guid}/work-orders"),
            ("POST", "/api/v1/operations/flights/{flightId:guid}/return-to-ramps"),
            ("POST", "/api/v1/operations/work-orders/from-scratch"),
            ("POST", "/api/v1/operations/work-orders/{id:guid}/return-to-ramps"),
            ("PUT", "/api/v1/operations/work-orders/{id:guid}"),
            ("POST", "/api/v1/mobile/flights/{flightId:guid}/work-orders"),
            ("POST", "/api/v1/mobile/work-orders/scratch"),
            ("PUT", "/api/v1/mobile/work-orders/{workOrderId:guid}"),
            ("POST", "/api/v1/mobile/work-orders/{workOrderId:guid}/return-to-ramp")
        };

        foreach (var expected in expectedRoutes)
        {
            var endpoint = endpoints.Single(item =>
                item.RoutePattern.RawText == expected.Route &&
                item.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(expected.Method) == true);
            var limit = endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>();

            limit.ShouldNotBeNull();
            limit!.MaxRequestBodySize.ShouldBe(WorkOrderInlineFilePolicy.MaxJsonRequestBytes);
        }
    }

    private class ThrowingProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException();
    }
}
