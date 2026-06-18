namespace BuildingBlocks.Infrastructure.Email;

/// <summary>
/// Strongly-typed binding for the <c>EmailSettings</c> configuration section. Values come from
/// <c>appsettings.json</c>; toggling <see cref="EnableEmailNotifications"/> off causes the SMTP
/// sender to skip the wire call entirely (useful for local development).
/// </summary>
public sealed class EmailSettings
{
    public const string SectionName = "EmailSettings";

    public bool EnableEmailNotifications { get; set; }

    public string DefaultFromEmail { get; set; } = "";

    public string? DefaultFromDisplayName { get; set; }

    public SmtpSettings SmtpSettings { get; set; } = new();
}

public sealed class SmtpSettings
{
    public string Server { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
