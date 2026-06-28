using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using MasterData.Application.Authorization;
using MasterData.Application.Contracts;
using MasterData.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Customers;

// --- Paged list -----------------------------------------------------------

public sealed record GetCustomersQuery(int Page = 1, int PageSize = 20, string? Search = null, bool? IsActive = null, Guid? CountryId = null, string? Sort = null)
    : IQuery<PagedResult<CustomerListItemDto>>;

public sealed class GetCustomersQueryHandler(IMasterDataDbContext db, IMasterDataScope scope)
    : IQueryHandler<GetCustomersQuery, PagedResult<CustomerListItemDto>>
{
    public async Task<Result<PagedResult<CustomerListItemDto>>> Handle(GetCustomersQuery request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = db.Customers.AsNoTracking();

        // Customer contacts only ever see their own customer; station staff see none.
        if (!resolved.Value.IsAdministrator)
        {
            if (resolved.Value.CustomerId is not { } scopedCustomer)
                return new PagedResult<CustomerListItemDto>([], page, pageSize, 0);
            query = query.Where(c => c.Id == scopedCustomer);
        }

        if (request.IsActive is { } active)
            query = query.Where(c => c.IsActive == active);

        if (request.CountryId is { } countryId)
            query = query.Where(c => c.CountryId == countryId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            var upper = term.ToUpperInvariant();
            query = query.Where(c => c.Name.Contains(term) || (c.IataCode != null && c.IataCode.Contains(upper)) || (c.IcaoCode != null && c.IcaoCode.Contains(upper)));
        }

        var total = await query.LongCountAsync(cancellationToken);

        var items = await ApplySort(query, request.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerListItemDto(
                c.Id, c.IataCode, c.IcaoCode, c.Name, c.CountryId,
                db.Countries.Where(co => co.Id == c.CountryId).Select(co => co.Name).FirstOrDefault() ?? string.Empty,
                c.IsActive,
                c.Contacts.Count(ct => ct.IsActive)))
            .ToListAsync(cancellationToken);

        return new PagedResult<CustomerListItemDto>(items, page, pageSize, total);
    }

    private static IQueryable<Customer> ApplySort(IQueryable<Customer> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return query.OrderBy(c => c.IataCode);

        return spec.Field switch
        {
            "iatacode" => spec.Descending ? query.OrderByDescending(c => c.IataCode) : query.OrderBy(c => c.IataCode),
            "name" => spec.Descending ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
            "isactive" => spec.Descending ? query.OrderByDescending(c => c.IsActive) : query.OrderBy(c => c.IsActive),
            _ => query.OrderBy(c => c.IataCode)
        };
    }
}

// --- By id ----------------------------------------------------------------

public sealed record GetCustomerByIdQuery(Guid Id) : IQuery<CustomerDto>;

public sealed class GetCustomerByIdQueryHandler(IMasterDataDbContext db, IMasterDataScope scope)
    : IQueryHandler<GetCustomerByIdQuery, CustomerDto>
{
    public async Task<Result<CustomerDto>> Handle(GetCustomerByIdQuery request, CancellationToken cancellationToken)
    {
        var scopeCheck = await scope.CheckCustomerAsync(request.Id, cancellationToken);
        if (scopeCheck.IsFailure)
            return scopeCheck.Error;

        var customer = await db.Customers.AsNoTracking()
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (customer is null)
            return Error.NotFound("Customer not found.", "MasterData.Customer.NotFound");

        var countryName = await db.Countries.AsNoTracking()
            .Where(c => c.Id == customer.CountryId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var contacts = customer.Contacts
            .OrderByDescending(ct => ct.IsActive)
            .ThenBy(ct => ct.Name)
            .Select(ct => new CustomerContactDto(
                ct.Id, ct.Name, ct.JobTitle, ct.Email, ct.Phone, ct.LinkedUserId, ct.IsActive, ct.CreatedAtUtc, ct.UpdatedAtUtc))
            .ToList();

        return new CustomerDto(
            customer.Id, customer.IataCode, customer.IcaoCode, customer.Name,
            customer.CountryId, countryName, customer.OfficialEmail, customer.OfficialPhone, customer.LogoFileReference,
            new AddressDto(customer.Address.Line1, customer.Address.Line2, customer.Address.City, customer.Address.Region, customer.Address.PostalCode),
            customer.IsActive, customer.CreatedAtUtc, customer.UpdatedAtUtc, Convert.ToBase64String(customer.RowVersion),
            contacts);
    }
}

public sealed record CustomerLogoContent(byte[] Content, string ContentType);
public sealed record GetCustomerLogoQuery(Guid Id) : IQuery<CustomerLogoContent>;

public sealed class GetCustomerLogoQueryHandler(IMasterDataDbContext db, IMasterDataScope scope, IFileStorage storage)
    : IQueryHandler<GetCustomerLogoQuery, CustomerLogoContent>
{
    public async Task<Result<CustomerLogoContent>> Handle(GetCustomerLogoQuery request, CancellationToken cancellationToken)
    {
        var scopeCheck = await scope.CheckCustomerAsync(request.Id, cancellationToken);
        if (scopeCheck.IsFailure)
            return scopeCheck.Error;

        var reference = await db.Customers.AsNoTracking()
            .Where(c => c.Id == request.Id)
            .Select(c => c.LogoFileReference)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(reference))
            return Error.NotFound("Customer logo not found.", "MasterData.Customer.LogoNotFound");

        await using var stream = await storage.OpenAsync(reference, cancellationToken);
        if (stream is null)
            return Error.NotFound("Customer logo file not found.", "MasterData.Customer.LogoFileNotFound");

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return new CustomerLogoContent(memory.ToArray(), ContentType(reference));
    }

    private static string ContentType(string reference) => Path.GetExtension(reference).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream"
    };
}

// --- Active options (for pickers) -----------------------------------------

public sealed record GetActiveCustomerOptionsQuery : IQuery<IReadOnlyList<CustomerOptionDto>>;

public sealed class GetActiveCustomerOptionsQueryHandler(IMasterDataDbContext db, IMasterDataScope scope)
    : IQueryHandler<GetActiveCustomerOptionsQuery, IReadOnlyList<CustomerOptionDto>>
{
    public async Task<Result<IReadOnlyList<CustomerOptionDto>>> Handle(GetActiveCustomerOptionsQuery request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        var query = db.Customers.AsNoTracking().Where(c => c.IsActive);

        if (!resolved.Value.IsAdministrator)
        {
            if (resolved.Value.CustomerId is not { } scopedCustomer)
                return Result.Success<IReadOnlyList<CustomerOptionDto>>([]);
            query = query.Where(c => c.Id == scopedCustomer);
        }

        IReadOnlyList<CustomerOptionDto> options = await query
            .OrderBy(c => c.IataCode)
            .Select(c => new CustomerOptionDto(c.Id, c.IataCode, c.Name))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }
}
