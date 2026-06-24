using BuildingBlocks.Api.Authorization;
using BuildingBlocks.Api.Concurrency;
using BuildingBlocks.Api.Results;
using BuildingBlocks.Application.Persistence;
using MasterData.Application.Features.Customers;
using MasterData.Application.Features.PortalAccess;
using MasterData.Domain.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MasterData.Api.Endpoints;

internal static class CustomerEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        var customers = group.MapGroup("/customers").WithTags("MasterData.Customers");

        customers.MapGet("/", async (ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? search = null, bool? isActive = null, Guid? countryId = null, string? sort = null) =>
        {
            var result = await sender.Send(new GetCustomersQuery(page, pageSize, search, isActive, countryId, sort), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Customers.View);

        customers.MapGet("/options", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetActiveCustomerOptionsQuery(), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Customers.View);

        customers.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCustomerByIdQuery(id), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Customers.View);

        customers.MapPost("/", async (CreateCustomerRequest request, ISender sender, CancellationToken ct) =>
        {
            var command = new CreateCustomerCommand(
                request.IataCode, request.IcaoCode, request.Name, request.CountryId,
                request.OfficialEmail, request.OfficialPhone,
                new CustomerAddressInput(request.Address.Line1, request.Address.Line2, request.Address.City, request.Address.Region, request.Address.PostalCode),
                MapContacts(request.Contacts));

            var result = await sender.Send(command, ct);
            return result.ToCreated(id => $"/api/v1/masterdata/customers/{id}");
        }).RequirePermission(MasterDataPermissions.Customers.Create);

        customers.MapPut("/{id:guid}", async (Guid id, UpdateCustomerRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var command = new UpdateCustomerCommand(
                id, request.IataCode, request.IcaoCode, request.Name, request.CountryId,
                request.OfficialEmail, request.OfficialPhone,
                new CustomerAddressInput(request.Address.Line1, request.Address.Line2, request.Address.City, request.Address.Region, request.Address.PostalCode),
                rowVersion);

            var result = await sender.Send(command, ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Customers.Update);

        customers.MapPost("/{id:guid}/contacts", async (Guid id, AddCustomerContactRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new AddCustomerContactCommand(id, request.Name, request.JobTitle, request.Email, request.Phone, rowVersion), ct);
            return result.ToCreated(contactId => $"/api/v1/masterdata/customers/{id}/contacts/{contactId}");
        }).RequirePermission(MasterDataPermissions.CustomerContacts.Create);

        customers.MapPut("/{id:guid}/contacts/{contactId:guid}", async (Guid id, Guid contactId, UpdateCustomerContactRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new UpdateCustomerContactCommand(id, contactId, request.Name, request.JobTitle, request.Email, request.Phone, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.CustomerContacts.Update);

        customers.MapPost("/{id:guid}/logo", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            if (!http.HasFormContentType)
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("A multipart form with a logo file is required.", "MasterData.Customer.LogoMissing"));

            var form = await http.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return ApiResults.Problem(BuildingBlocks.Domain.Results.Error.Validation("A logo file is required.", "MasterData.Customer.LogoMissing"));

            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);

            var result = await sender.Send(
                new SetCustomerLogoCommand(id, memory.ToArray(), file.FileName, file.ContentType, rowVersion), ct);
            return result.ToOk();
        }).RequirePermission(MasterDataPermissions.Customers.Update).DisableAntiforgery();

        customers.MapPost("/{id:guid}/activate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new ActivateCustomerCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Customers.Activate);

        customers.MapPost("/{id:guid}/deactivate", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new DeactivateCustomerCommand(id, rowVersion), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.Customers.Deactivate);

        customers.MapPost("/{id:guid}/contacts/{contactId:guid}/grant-access",
            async (Guid id, Guid contactId, GrantPortalAccessRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GrantContactPortalAccessCommand(id, contactId, request.RoleId), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.CustomerContacts.GrantAccess);

        customers.MapPost("/{id:guid}/contacts/{contactId:guid}/remove",
            async (Guid id, Guid contactId, HttpRequest http, ISender sender, CancellationToken ct, bool releaseEmail = false) =>
        {
            if (http.GetIfMatch() is not { } rowVersion)
                return ApiResults.Problem(ConcurrencyErrors.PreconditionRequired);

            var result = await sender.Send(new RemoveCustomerContactCommand(id, contactId, rowVersion, releaseEmail), ct);
            return result.ToNoContent();
        }).RequirePermission(MasterDataPermissions.CustomerContacts.Remove);
    }

    private static IReadOnlyList<CustomerContactInput> MapContacts(IReadOnlyList<CustomerContactRequest>? contacts) =>
        contacts is null
            ? []
            : contacts.Select(c => new CustomerContactInput(c.Id, c.Name, c.JobTitle, c.Email, c.Phone)).ToList();
}
