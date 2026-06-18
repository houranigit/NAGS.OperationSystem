using BuildingBlocks.Application.Abstractions.Queries;
using Core.Contracts.Features.Customer;

namespace Core.Application.Features.Customer.Queries.GetCustomerById;

/// <summary>Single-customer lookup used by the customer profile page.</summary>
public sealed record GetCustomerByIdQuery(Guid Id) : IQuery<CustomerDto?>;
