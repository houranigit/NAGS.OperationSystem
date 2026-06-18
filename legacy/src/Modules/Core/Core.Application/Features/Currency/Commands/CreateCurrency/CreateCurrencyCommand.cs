using BuildingBlocks.Application.Abstractions.Commands;
using Core.Contracts.Features.Currency;

namespace Core.Application.Features.Currency.Commands.CreateCurrency;

public sealed record CreateCurrencyCommand(
    string Code,
    string Name,
    bool IsActive,
    IReadOnlyList<ExchangeRateInput> ExchangeRates) : ICommand<Guid>;
