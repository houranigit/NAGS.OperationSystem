using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Country;
using Core.Domain.Aggregates.Customer;
using Core.Domain.ValueObjects;

namespace Core.Application.Features.Customer.Commands.CreateCustomer;

/// <summary>
/// Creates a customer. Uses <see cref="Core.Domain.Aggregates.Customer.Customer.SyncContacts"/> — reference implementation for commands that own child collections.
/// </summary>
/// <remarks>
/// Do not patch contacts individually from here; map Contracts inputs once and pass a full snapshot so the aggregate can update existing ids,
/// remove omitted ids, and add rows without persisted ids (null keys). Same orchestration shape as <see cref="Core.Application.Features.Customer.Commands.UpdateCustomer.UpdateCustomerCommandHandler"/>.
/// </remarks>
public sealed class CreateCustomerCommandHandler(
    ICustomerRepository customers,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<CreateCustomerCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var iataResult = IataAirlineCode.Create(request.IataCode);
        if (iataResult.IsFailure) return iataResult.Error;

        if (await customers.ExistsByIataCodeAsync(iataResult.Value.Value, cancellationToken))
            return Error.Conflict("A customer with this IATA code already exists.");

        if (request.OfficialEmail is not null &&
            await customers.ExistsByOfficialEmailAsync(request.OfficialEmail, null, cancellationToken))
            return Error.Conflict("A customer with this email already exists.");

        Address? address = null;
        if (request.Address is not null)
        {
            var addrResult = Address.Create(
                request.Address.Line1,
                request.Address.Line2,
                request.Address.City,
                request.Address.PostalCode,
                CountryId.From(request.Address.CountryId));
            if (addrResult.IsFailure) return addrResult.Error;
            address = addrResult.Value;
        }

        var created = Core.Domain.Aggregates.Customer.Customer.Create(
            iataResult.Value,
            request.Name,
            request.IcaoCode,
            request.OfficialEmail,
            request.OfficialPhone,
            request.LogoBytes,
            address);
        if (created.IsFailure) return created.Error;

        var customer = created.Value;

        var contactRows = request.Contacts
            .Select(c => ((Guid?)c.Id, c.Name, (string?)null, c.Email, c.Phone, c.CreateLinkedUserOnAdd))
            .ToList<(Guid? ContactId, string Name, string? JobTitle, string Email, string? Phone, bool CreateUserOnAdd)>();

        // Single reconciliation API — update/remove/add semantics owned by the aggregate (avoid incremental drift vs UI truth).
        var syncResult = customer.SyncContacts(contactRows);
        if (syncResult.IsFailure) return syncResult.Error;

        if (!request.IsActive)
        {
            var d = customer.Deactivate();
            if (d.IsFailure) return d.Error;
        }

        customers.Add(customer);
        MobileSyncCatalogBroadcasts.EnqueueRefresh(mobileSync, MobileSyncTables.Customers);
        return customer.Id.Value;
    }
}
