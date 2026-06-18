using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Country;
using Core.Domain.Aggregates.Customer;
using Core.Domain.ValueObjects;

namespace Core.Application.Features.Customer.Commands.UpdateCustomer;

/// <summary>
/// Updates a customer. Uses <see cref="Core.Domain.Aggregates.Customer.Customer.SyncContacts"/> — same child-collection snapshot pattern as create (reference for aggregates with children).
/// </summary>
/// <remarks>
/// The payload replaces contact membership logically: omitted persisted contacts are removed, matched ids are updated, null ids become inserts.
/// Copy this orchestration for other aggregates that expose editable collections from the UI/API (full snapshot + aggregate sync method).
/// </remarks>
public sealed class UpdateCustomerCommandHandler(
    ICustomerRepository customers,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<UpdateCustomerCommand>
{
    public async Task<Result> Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
    {
        var id = CustomerId.From(request.Id);
        var entity = await customers.GetByIdWithContactsAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("Customer was not found.");

        if (request.OfficialEmail is not null &&
            await customers.ExistsByOfficialEmailAsync(request.OfficialEmail, id, cancellationToken))
            return Error.Conflict("A customer with this email already exists.");

        var detailsResult = entity.UpdateDetails(
            request.Name,
            request.IcaoCode,
            request.OfficialEmail,
            request.OfficialPhone);
        if (detailsResult.IsFailure) return detailsResult;

        if (request.LogoBytes is not null)
        {
            var logoResult = entity.UpdateLogo(request.LogoBytes);
            if (logoResult.IsFailure) return logoResult;
        }

        if (request.Address is not null)
        {
            var addrResult = Address.Create(
                request.Address.Line1,
                request.Address.Line2,
                request.Address.City,
                request.Address.PostalCode,
                CountryId.From(request.Address.CountryId));
            if (addrResult.IsFailure) return addrResult;
            entity.UpdateAddress(addrResult.Value);
        }
        else
        {
            entity.UpdateAddress(null);
        }

        var contactRows = request.Contacts
            .Select(c => ((Guid?)c.Id, c.Name, (string?)null, c.Email, c.Phone, c.CreateLinkedUserOnAdd))
            .ToList<(Guid? ContactId, string Name, string? JobTitle, string Email, string? Phone, bool CreateUserOnAdd)>();

        var syncResult = entity.SyncContacts(contactRows);
        if (syncResult.IsFailure) return syncResult;

        if (request.IsActive != entity.IsActive)
        {
            var toggle = request.IsActive ? entity.Activate() : entity.Deactivate();
            if (toggle.IsFailure) return toggle;
        }

        customers.Update(entity);
        MobileSyncCatalogBroadcasts.EnqueueRefresh(mobileSync, MobileSyncTables.Customers);
        return Result.Success();
    }
}
