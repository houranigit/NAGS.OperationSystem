using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Currency;
using Core.Domain.ValueObjects;

namespace Core.Application.Features.Currency.Commands.CreateCurrency;

/// <summary>
/// Creates a currency and optional initial exchange-rate lines (see <see cref="Core.Domain.Aggregates.Currency.Currency.Create"/>).
/// </summary>
/// <remarks>
/// Maps Contracts <see cref="ExchangeRateInput"/> once; <see cref="TransactionBehavior"/> performs <c>SaveChanges</c>.
/// </remarks>
public sealed class CreateCurrencyCommandHandler(
    ICurrencyRepository currencies,
    ICurrentUserService currentUser)
    : ICommandHandler<CreateCurrencyCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCurrencyCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? Guid.Empty;

        var codeResult = CurrencyCode.Create(request.Code);
        if (codeResult.IsFailure) return codeResult.Error;

        if (await currencies.ExistsByCodeAsync(codeResult.Value.Value, cancellationToken))
            return Error.Conflict("A currency with this code already exists.");

        var initialRates = request.ExchangeRates
            .Select(r => (CurrencyId.From(r.ToCurrencyId), r.Rate))
            .ToList<(CurrencyId ToCurrencyId, decimal Rate)>();

        var created = Core.Domain.Aggregates.Currency.Currency.Create(codeResult.Value, request.Name, userId, initialRates);
        if (created.IsFailure) return created.Error;

        var currency = created.Value;

        if (!request.IsActive)
        {
            var d = currency.Deactivate();
            if (d.IsFailure) return d.Error;
        }

        currencies.Add(currency);
        return currency.Id.Value;
    }
}
