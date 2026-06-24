namespace BuildingBlocks.Application.Auditing;

/// <summary>
/// Central deny-list that keeps secrets out of the audit trail. Any property whose name contains
/// one of these fragments (case-insensitive) is dropped from captured changes entirely. This is
/// the single source of truth for redaction so automatic capture and explicit events agree.
/// </summary>
public static class AuditRedaction
{
    private static readonly string[] DeniedFragments =
    [
        "password",
        "passwordhash",
        "hash",
        "token",
        "secret",
        "salt",
        "recoverycode",
        "mfa",
        "credential",
        "securitystamp",
        "apikey"
    ];

    public static bool IsSensitive(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return false;

        foreach (var fragment in DeniedFragments)
        {
            if (propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
