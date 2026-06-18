using BuildingBlocks.Application.Abstractions.Commands;
using Core.Contracts.Features.Currency;

namespace Core.Application.Features.Currency.Commands.UpdateCurrency;

public sealed record UpdateCurrencyCommand(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    IReadOnlyList<ExchangeRateInput> ExchangeRates) : ICommand;
