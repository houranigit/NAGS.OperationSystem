using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Api;

public sealed class BrowserApiClientTests
{
    [Fact]
    public async Task GetAsync_returns_null_for_nullable_response_when_body_is_empty()
    {
        var api = NewClient(new StubJsRuntime(""));

        var result = await api.GetAsync<WorkOrderSummaryModel?>($"/operations/flights/{Guid.NewGuid()}/work-orders/mine");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Approved_work_order_download_uses_authenticated_blob_download_route()
    {
        var runtime = new CapturingDownloadJsRuntime();
        var operations = new OperationsApiClient(NewClient(runtime));
        var flightId = Guid.NewGuid();

        await operations.DownloadApprovedWorkOrderAsync(flightId);

        runtime.Identifier.ShouldBe("operationsSystem.api.downloadFile");
        runtime.Arguments.ShouldNotBeNull();
        runtime.Arguments![0].ShouldBe($"/operations/flights/{flightId}/work-orders/approved/pdf");
        runtime.Arguments[1].ShouldBe("approved-work-order.pdf");
    }

    [Fact]
    public async Task Service_line_attachment_upload_uses_service_line_route()
    {
        var runtime = new CapturingDownloadJsRuntime();
        var operations = new OperationsApiClient(NewClient(runtime));
        var workOrderId = Guid.NewGuid();
        var serviceLineId = Guid.NewGuid();

        await operations.UploadWorkOrderServiceLineAttachmentAsync(
            workOrderId,
            serviceLineId,
            "Image",
            [1, 2, 3],
            "service.jpg",
            "image/jpeg",
            "row-version");

        runtime.Identifier.ShouldBe("operationsSystem.api.uploadFile");
        runtime.Arguments.ShouldNotBeNull();
        runtime.Arguments![0].ShouldBe($"/operations/work-orders/{workOrderId}/service-lines/{serviceLineId}/attachments");
        runtime.Arguments[6].ShouldBe("row-version");
        ((IReadOnlyDictionary<string, string>)runtime.Arguments[7]!)["kind"].ShouldBe("Image");
    }

    [Fact]
    public async Task Service_line_attachment_download_uses_service_line_route()
    {
        var runtime = new CapturingDownloadJsRuntime();
        var operations = new OperationsApiClient(NewClient(runtime));
        var workOrderId = Guid.NewGuid();
        var serviceLineId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();

        await operations.DownloadWorkOrderServiceLineAttachmentAsync(workOrderId, serviceLineId, attachmentId);

        runtime.Identifier.ShouldBe("operationsSystem.api.requestFile");
        runtime.Arguments.ShouldNotBeNull();
        runtime.Arguments![0].ShouldBe($"/operations/work-orders/{workOrderId}/service-lines/{serviceLineId}/attachments/{attachmentId}");
    }

    [Fact]
    public async Task Service_line_attachment_delete_uses_service_line_route_and_row_version()
    {
        var runtime = new CapturingDownloadJsRuntime();
        var operations = new OperationsApiClient(NewClient(runtime));
        var workOrderId = Guid.NewGuid();
        var serviceLineId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();

        await operations.DeleteWorkOrderServiceLineAttachmentAsync(
            workOrderId,
            serviceLineId,
            attachmentId,
            "row-version");

        runtime.Identifier.ShouldBe("operationsSystem.api.request");
        runtime.Arguments.ShouldNotBeNull();
        runtime.Arguments![0].ShouldBe("DELETE");
        runtime.Arguments[1].ShouldBe($"/operations/work-orders/{workOrderId}/service-lines/{serviceLineId}/attachments/{attachmentId}");
        runtime.Arguments[5].ShouldBe("row-version");
    }

    [Fact]
    public async Task Change_user_access_forwards_role_and_row_version()
    {
        var runtime = new CapturingDownloadJsRuntime();
        var identity = new IdentityApiClient(NewClient(runtime));
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await identity.ChangeUserAccessAsync(
            userId,
            new AssignRoleRequest(roleId),
            "user-row-version");

        runtime.Identifier.ShouldBe("operationsSystem.api.request");
        runtime.Arguments.ShouldNotBeNull();
        runtime.Arguments![0].ShouldBe("PUT");
        runtime.Arguments[1].ShouldBe($"/identity/users/{userId}/role");
        ((AssignRoleRequest)runtime.Arguments[2]!).RoleId.ShouldBe(roleId);
        runtime.Arguments[5].ShouldBe("user-row-version");
    }

    [Fact]
    public async Task Direct_refresh_retries_once_when_another_tab_consumed_the_predecessor()
    {
        var runtime = new ConsumedThenSuccessJsRuntime();
        var api = NewClient(runtime);

        var token = await api.PostAsync<object, AccessTokenResponse>(
            "/identity/auth/refresh",
            new { });

        token.AccessToken.ShouldBe("successor-access-token");
        runtime.CallCount.ShouldBe(2);
    }

    private static BrowserApiClient NewClient(IJSRuntime jsRuntime)
    {
        var tokenStore = new AuthTokenStore();
        var locale = new LocaleState(jsRuntime);
        var refresher = new ClientTokenRefresher(jsRuntime, tokenStore, locale);
        return new BrowserApiClient(jsRuntime, tokenStore, locale, refresher);
    }

    private sealed class StubJsRuntime(string response) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (typeof(TValue) == typeof(string))
                return ValueTask.FromResult((TValue)(object)response);

            throw new InvalidOperationException($"Unexpected JS interop return type {typeof(TValue).Name}.");
        }
    }

    private sealed class CapturingDownloadJsRuntime : IJSRuntime
    {
        public string? Identifier { get; private set; }
        public object?[]? Arguments { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            Identifier = identifier;
            Arguments = args;
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    private sealed class ConsumedThenSuccessJsRuntime : IJSRuntime
    {
        public int CallCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            object?[]? args) =>
            InvokeAsync<TValue>(
                identifier,
                CancellationToken.None,
                args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            CallCount++;
            if (CallCount == 1)
            {
                var body = JsonSerializer.Serialize(new
                {
                    code = ClientTokenRefresher.ConsumedRefreshTokenProblemCode
                });
                var error = JsonSerializer.Serialize(new
                {
                    status = 401,
                    body
                });
                throw new JSException(error);
            }

            var response = JsonSerializer.Serialize(new AccessTokenResponse(
                "successor-access-token",
                DateTimeOffset.UtcNow.AddMinutes(15)));
            return ValueTask.FromResult((TValue)(object)response);
        }
    }
}
