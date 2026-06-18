using Core.Application.Features.Currency.Commands.CreateCurrency;
using Core.Application.Features.Currency.Commands.UpdateCurrency;
using Core.Contracts.Features.Currency;

namespace Host.Web.Components.Pages.Settings.MasterData.Currencies.Dialog;

/// <summary>
/// UI form state for Currency Add/Update dialogs. Maps to Create/Update commands (same pattern as the Customer feature form model).
/// </summary>
public sealed class CurrencyFormModel
{
    public Guid? Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;

    /// <summary>Editable cross-rate rows (full snapshot for create/update).</summary>
    public List<CurrencyExchangeRateEditorLine> RateLines { get; set; } = [];

    public static CurrencyFormModel FromDto(CurrencyDto dto) =>
        new()
        {
            Id = dto.Id,
            Code = dto.Code,
            Name = dto.Name,
            IsActive = dto.IsActive,
            RateLines = dto.ExchangeRates.Count == 0
                ? []
                : dto.ExchangeRates.Select(CurrencyExchangeRateEditorLine.FromDto).ToList()
        };

    public CurrencyFormModel Clone() =>
        new()
        {
            Id = Id,
            Code = Code,
            Name = Name,
            IsActive = IsActive,
            RateLines = RateLines.Select(l => l.Clone()).ToList()
        };

    /// <summary>True if any field on the line is set — then the row must be complete and valid.</summary>
    public static bool RateLineHasAnyField(CurrencyExchangeRateEditorLine line) =>
        line.ToCurrencyId is not null
        || line.Rate is { } r && r != 0m;

    /// <summary>Ready to send to the API (domain requires positive rate and target id).</summary>
    public static bool RateLineIsComplete(CurrencyExchangeRateEditorLine line) =>
        line.ToCurrencyId is not null
        && line.Rate is { } r
        && r > 0m
        && r <= 1_000_000m;

    public bool IsRateLineToCurrencyValidForRow(int index)
    {
        if (index < 0 || index >= RateLines.Count) return true;
        var line = RateLines[index];
        if (!RateLineHasAnyField(line)) return true;
        return line.ToCurrencyId is not null;
    }

    public bool IsRateLineRateValidForRow(int index)
    {
        if (index < 0 || index >= RateLines.Count) return true;
        var line = RateLines[index];
        if (!RateLineHasAnyField(line)) return true;
        if (line.Rate is null) return false;
        var r = line.Rate.Value;
        return r > 0m && r <= 1_000_000m;
    }

    public bool AreTargetCurrenciesUnique()
    {
        var targets = RateLines
            .Where(RateLineIsComplete)
            .Select(l => l.ToCurrencyId!.Value)
            .ToList();
        return targets.Count == targets.Distinct().Count();
    }

    public IReadOnlyList<ExchangeRateInput> BuildExchangeRateInputs() =>
        RateLines
            .Where(RateLineIsComplete)
            .Select(l => new ExchangeRateInput(l.PersistedRateId, l.ToCurrencyId!.Value, l.Rate!.Value))
            .ToList();

    public CreateCurrencyCommand ToCreateCurrencyCommand() =>
        new(Code.Trim(), Name.Trim(), IsActive, BuildExchangeRateInputs());

    public UpdateCurrencyCommand ToUpdateCurrencyCommand(Guid id) =>
        new(id, Code.Trim(), Name.Trim(), IsActive, BuildExchangeRateInputs());
}

/// <summary>One outbound exchange-rate row in the dialog (add / update).</summary>
public sealed class CurrencyExchangeRateEditorLine
{
    /// <summary>Null when the row was never persisted (insert on update).</summary>
    public Guid? PersistedRateId { get; set; }

    public Guid? ToCurrencyId { get; set; }

    public decimal? Rate { get; set; }

    public static CurrencyExchangeRateEditorLine FromDto(ExchangeRateDto dto) =>
        new()
        {
            PersistedRateId = dto.Id,
            ToCurrencyId = dto.ToCurrencyId,
            Rate = dto.Rate,
            PersistedEffectiveAtUtc = dto.CreatedAt
        };

    public CurrencyExchangeRateEditorLine Clone() =>
        new()
        {
            PersistedRateId = PersistedRateId,
            ToCurrencyId = ToCurrencyId,
            Rate = Rate,
            PersistedEffectiveAtUtc = PersistedEffectiveAtUtc
        };

    /// <summary>Shown only for rows loaded from the server (effective / created).</summary>
    public DateTime? PersistedEffectiveAtUtc { get; set; }
}
