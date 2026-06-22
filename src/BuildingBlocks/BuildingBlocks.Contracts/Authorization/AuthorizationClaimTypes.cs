namespace BuildingBlocks.Contracts.Authorization;

/// <summary>
/// Claim types shared across modules so the issuer (Identity) and consumers (every module's
/// data-scope checks) agree on how the account's business identity travels in the access token.
/// </summary>
public static class AuthorizationClaimTypes
{
    /// <summary>The account's <see cref="UserType"/> name. Drives server-side data scope.</summary>
    public const string UserType = "user_type";

    /// <summary>
    /// The MasterData record the account represents (StaffMember or CustomerContact id).
    /// Absent for a System Administrator.
    /// </summary>
    public const string ExternalReference = "external_ref";
}
