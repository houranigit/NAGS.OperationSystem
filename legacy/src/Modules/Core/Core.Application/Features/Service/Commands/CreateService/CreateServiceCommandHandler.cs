using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Service;

namespace Core.Application.Features.Service.Commands.CreateService;

/// <summary>
/// Creates a service — orchestrates domain <see cref="Core.Domain.Aggregates.Service.Service"/>; persistence is flushed by transaction behavior (<c>SaveChanges</c> not here).
/// </summary>
/// <remarks>Shape mirrors <see cref="Core.Application.Features.Customer.Commands.CreateCustomer.CreateCustomerCommandHandler"/> sans child collections.</remarks>
public sealed class CreateServiceCommandHandler(
    IServiceRepository services,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<CreateServiceCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateServiceCommand request, CancellationToken cancellationToken)
    {
        if (await services.ExistsByNameAsync(request.Name, cancellationToken))
            return Error.Conflict("A service with this name already exists.");

        var created = Core.Domain.Aggregates.Service.Service.Create(request.Name, request.Description);
        if (created.IsFailure) return created.Error;

        var service = created.Value;

        if (!request.IsActive)
        {
            var d = service.Deactivate();
            if (d.IsFailure) return d.Error;
        }

        services.Add(service);
        MobileSyncCatalogBroadcasts.EnqueueRefresh(mobileSync, MobileSyncTables.Services);
        return service.Id.Value;
    }
}
