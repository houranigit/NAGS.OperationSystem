namespace Core.Contracts.Features.Country;

public sealed record CountrySelectOption(
    Guid Id,
    string Code,
    string Name)
{
    /// <summary>Dropdown text: non-empty Name, else Code, else <see cref="Id"/> string.</summary>
    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(Name) ? Name.Trim()
        : !string.IsNullOrWhiteSpace(Code) ? Code.Trim()
        : Id.ToString();
}
