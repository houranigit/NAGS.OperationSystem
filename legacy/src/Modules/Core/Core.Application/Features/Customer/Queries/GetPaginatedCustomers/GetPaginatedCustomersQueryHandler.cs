using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Core.Application.Abstractions;
using Core.Contracts.Features.Country;
using Core.Contracts.Features.Customer;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace Core.Application.Features.Customer.Queries.GetPaginatedCustomers;

/// <summary>
/// Reference handler for paginated **grid** queries: stay on <see cref="IQueryable{T}"/> until one final materialization.
/// </summary>
/// <remarks>
/// <para><b>Wrong:</b> <c>ToListAsync()</c> / materializing the entire DbSet (or several tables) <i>before</i> <c>Skip</c>/<c>Take</c>, then paging in memory.</para>
/// <para><b>Right:</b> filter → count → order → page → project to DTO in one <c>Select</c> → <c>ToListAsync</c> once (same pipeline as <c>GetPaginatedCustomerSelectOptionsQueryHandler</c>).</para>
/// <para><b>Includes / children:</b> use EF <c>Include</c>/<c>ThenInclude</c> only as needed for shapes EF can translate; compose the DTO inside <c>Select</c> so paging applies to the root aggregate query.
/// Handlers that stitch many tables in memory (e.g. employees + licenses index) need a deliberate pattern — do not copy those until refactored.</para>
/// </remarks>
public sealed class GetPaginatedCustomersQueryHandler(ICoreDbContext db)
    : IQueryHandler<GetPaginatedCustomersQuery, PaginatedResult<CustomerDto>>
{
    public async Task<Result<PaginatedResult<CustomerDto>>> Handle(
        GetPaginatedCustomersQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Root query — Include navigation paths required by the projection below (EF translates to SQL joins).
        var query = db.Customers
            .Include(x => x.Address)
            .ThenInclude(a => a!.Country)
            .Include(x => x.Contacts)
            .AsQueryable();

        // 2. Dynamic filters — apply to entity/query shape (same names as grid columns expect).
        if (!string.IsNullOrWhiteSpace(request.FilterQuery))
            query = query.Where(request.FilterQuery);

        // 3. Total count before paging (still IQueryable; no full-table client materialize).
        var total = query.Count();

        // 4. Sort — caller OrderByQuery or default.
        query = !string.IsNullOrWhiteSpace(request.OrderByQuery)
            ? query.OrderBy(request.OrderByQuery)
            : query.OrderBy(x => x.Name);

        // 5–7. Page in the database, map to CustomerDto (nested collections inside Select), single ToListAsync.
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
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
            .ToListAsync(cancellationToken);

        return new PaginatedResult<CustomerDto>(items, total, request.Page, request.PageSize);
    }
}
