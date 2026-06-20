namespace Identity.Application;

/// <summary>Configuration for the Identity module, bound from the "Identity" configuration section.</summary>
public sealed class IdentityModuleOptions
{
    public const string SectionName = "Identity";

    public JwtOptions Jwt { get; set; } = new();
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
    public int MaxFailedSignInAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int InvitationExpiryHours { get; set; } = 72;

    public AdminBootstrapOptions Admin { get; set; } = new();
    public DemoDataOptions DemoData { get; set; } = new();
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "operations-system";
    public string Audience { get; set; } = "operations-system";
    public string SigningKey { get; set; } = string.Empty;
}

public sealed class AdminBootstrapOptions
{
    public string Email { get; set; } = "admin@nags.sa";
    public string DisplayName { get; set; } = "System Administrator";
    public string Password { get; set; } = string.Empty;
}

/// <summary>Development-only bulk data for exercising pagination in the portal.</summary>
public sealed class DemoDataOptions
{
    public bool Enabled { get; set; }
    public int RoleCount { get; set; } = 55;
    public int UserCount { get; set; } = 55;
}
