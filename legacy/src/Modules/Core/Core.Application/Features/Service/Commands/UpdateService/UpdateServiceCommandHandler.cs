using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Service;

namespace Core.Application.Features.Service.Commands.UpdateService;

/// <summary>
/// Updates service details and active flag; no <c>SaveChanges</c> in handler — see <see cref="Core.Application.Features.Customer.Commands.UpdateCustomer.UpdateCustomerCommandHandler"/>.
/// </summary>
public sealed class UpdateServiceCommandHandler(
    IServiceRepository services,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<UpdateServiceCommand>
{
    public async Task<Result> Handle(UpdateServiceCommand request, CancellationToken cancellationToken)
    {
        var id = ServiceId.From(request.Id);
        var entity = await services.GetByIdAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("Service was not found.");

        var detailsResult = entity.UpdateDetails(request.Name, request.Description);
        if (detailsResult.IsFailure) return detailsResult;

        if (request.IsActive != entity.IsActive)
        {
            var toggle = request.IsActive ? entity.Activate() : entity.Deactivate();
            if (toggle.IsFailure) return toggle;
        }

        services.Update(entity);
        MobileSyncCatalogBroadcasts.EnqueueRefresh(mobileSync, MobileSyncTables.Services);
        return Result.Success();
    }
}
