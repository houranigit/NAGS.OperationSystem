namespace BuildingBlocks.Infrastructure.Email;

/// <summary>
/// SMTP/email settings. Disabled by default so no SMTP connection is attempted in development or
/// tests. Secrets (password) come from user-secrets/environment, never tracked appsettings.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "EmailSettings";

    public bool EnableEmailNotifications { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromEmail { get; set; } = "no-reply@operationssystem.local";
    public string FromName { get; set; } = "Operations System";
}
