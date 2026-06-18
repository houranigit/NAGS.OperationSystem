using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;

namespace Core.Domain.Aggregates.Currency;

public sealed class ExchangeRate : Entity<ExchangeRateId>
{
    public CurrencyId CurrencyId { get; private set; } = null!;
    public CurrencyId ToCurrencyId { get; private set; } = null!;

    /// <summary>
    /// <see cref="ToCurrencyId"/> row; populated when loaded with Include (EF queries). Domain mutators use <see cref="ToCurrencyId"/> only.
    /// </summary>
    public Currency? TargetCurrency { get; private set; }
    public decimal Rate { get; private set; }
    public Guid CreatedById { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private ExchangeRate() { }

    internal static Result<ExchangeRate> Create(
        CurrencyId currencyId,
        CurrencyId toCurrencyId,
        decimal rate,
        Guid createdById)
    {
        if (rate <= 0)
            return Error.Validation("Exchange rate must be greater than zero.");

        if (rate > 1_000_000)
            return Error.Validation("Exchange rate exceeds the allowed maximum.");

        if (createdById == Guid.Empty)
            return Error.Validation("CreatedById is required.");

        return new ExchangeRate
        {
            Id = ExchangeRateId.New(),
            CurrencyId = currencyId,
            ToCurrencyId = toCurrencyId,
            Rate = rate,
            CreatedById = createdById,
            CreatedAt = DateTime.UtcNow
        };
    }

    public Result UpdateRate(decimal newRate)
    {
        if (newRate <= 0)
            return Error.Validation("Exchange rate must be greater than zero.");

        if (newRate > 1_000_000)
            return Error.Validation("Exchange rate exceeds the allowed maximum.");

        Rate = newRate;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public decimal Convert(decimal amount) => amount * Rate;
}
