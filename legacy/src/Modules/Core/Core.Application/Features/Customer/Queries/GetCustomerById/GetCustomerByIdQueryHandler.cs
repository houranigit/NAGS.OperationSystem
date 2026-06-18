using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.Country;
using Core.Contracts.Features.Customer;
using Core.Domain.Aggregates.Customer;
using Microsoft.EntityFrameworkCore;

namespace Core.Application.Features.Customer.Queries.GetCustomerById;

public sealed class GetCustomerByIdQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetCustomerByIdQuery, CustomerDto?>
{
    public async Task<Result<CustomerDto?>> Handle(GetCustomerByIdQuery request, CancellationToken cancellationToken)
    {
        if (request.Id == Guid.Empty)
            return (CustomerDto?)null;

        var customerId = CustomerId.From(request.Id);
        var dto = await db.Customers
            .Where(c => c.Id == customerId)
            .Include(c => c.Address).ThenInclude(a => a!.Country)
            .Include(c => c.Contacts)
            .Select(c => new CustomerDto(
                c.Id.Value,
                c.IataCode.Value,
                c.IcaoCode,
                c.Name,
                c.OfficialEmail,
                c.OfficialPhone,
                c.Address == null
                    ? null
                    : new AddressDto(
                        c.Address.Line1,
                        c.Address.Line2,
                        c.Address.City,
                        c.Address.PostalCode,
                        c.Address.Country == null
                            ? null
                            : new CountryDto(
                                c.Address.CountryId.Value,
                                c.Address.Country.Code.Value,
                                c.Address.Country.Name,
                                c.Address.Country.IsActive,
                                c.Address.Country.CreatedAt,
                                null)),
                c.IsActive,
                c.CreatedAt,
                c.UpdatedAt,
                c.Contacts.Select(cc =>
                        new CustomerContactDto(cc.Id.Value, cc.Name, cc.Email, cc.Phone, cc.LinkedUserId, cc.IsActive))
                    .ToList(),
                c.Logo))
            .FirstOrDefaultAsync(cancellationToken);

        return dto;
    }
}
