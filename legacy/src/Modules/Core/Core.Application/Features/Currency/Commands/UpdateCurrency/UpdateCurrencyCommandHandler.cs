using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Currency;

namespace Core.Application.Features.Currency.Commands.UpdateCurrency;

/// <summary>
/// Updates currency name, active flag, and exchange-rate snapshot via aggregate <c>SyncExchangeRates</c>.
/// </summary>
/// <remarks>
/// ISO code is immutable after creation; <see cref="UpdateCurrencyCommand.Code"/> must match the persisted code.
/// </remarks>
public sealed class UpdateCurrencyCommandHandler(
    ICurrencyRepository currencies,
    ICurrentUserService currentUser)
    : ICommandHandler<UpdateCurrencyCommand>
{
    public async Task<Result> Handle(UpdateCurrencyCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
            return Error.Validation("Current user is required.");

        var userId = currentUser.UserId.Value;

        var id = CurrencyId.From(request.Id);
        var entity = await currencies.GetByIdWithRatesAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("Currency was not found.");

        if (!string.Equals(entity.Code.Value, request.Code.Trim(), StringComparison.OrdinalIgnoreCase))
            return Error.Validation("Currency code cannot be changed after creation.");

        var nameResult = entity.UpdateDetails(request.Name);
        if (nameResult.IsFailure) return nameResult;

        var incomingRates = request.ExchangeRates
            .Select(r => (r.Id, CurrencyId.From(r.ToCurrencyId), r.Rate))
            .ToList<(Guid? ExistingRateId, CurrencyId ToCurrencyId, decimal Rate)>();

        var syncResult = entity.SyncExchangeRates(incomingRates, userId);
        if (syncResult.IsFailure) return syncResult;

        if (request.IsActive != entity.IsActive)
        {
            var toggle = request.IsActive ? entity.Activate() : entity.Deactivate();
            if (toggle.IsFailure) return toggle;
        }

        currencies.Update(entity);
        return Result.Success();
    }
}
