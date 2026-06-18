namespace Host.Web.Configuration;

public sealed class PlatformSettings
{
    public const string SectionName = "PlatformSettings";

    /// <summary>Default / display currency code for UI (e.g. SAR).</summary>
    public string CurrencyCode { get; set; } = "SAR";

    /// <summary>Public-facing product name used in transactional emails.</summary>
    public string AppName { get; set; } = "NAGS Operations";

    /// <summary>Base URL of the portal (used to build email links — e.g. activation links).</summary>
    public string PortalBaseUrl { get; set; } = "";
}
