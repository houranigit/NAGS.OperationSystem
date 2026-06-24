namespace BuildingBlocks.Api.RateLimiting;

/// <summary>Named rate-limiting policies shared between the host (registration) and module endpoints.</summary>
public static class RateLimitPolicies
{
    /// <summary>Applied to anonymous, abuse-prone auth endpoints (login, refresh, activate, etc.).</summary>
    public const string AnonymousAuth = "anonymous-auth";
}
