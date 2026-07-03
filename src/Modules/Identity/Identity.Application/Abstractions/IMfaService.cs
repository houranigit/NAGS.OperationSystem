namespace Identity.Application.Abstractions;

/// <summary>TOTP authenticator operations (RFC 6238) plus recovery-code generation.</summary>
public interface IMfaService
{
    /// <summary>Generates a new Base32 TOTP shared secret.</summary>
    public string GenerateSecret();

    /// <summary>Builds the otpauth:// URI an authenticator app scans to enroll.</summary>
    public string BuildOtpAuthUri(string secret, string accountName);

    /// <summary>Verifies a 6-digit TOTP code against the secret, allowing a small clock skew.</summary>
    public bool VerifyCode(string secret, string code, DateTimeOffset now);

    /// <summary>Generates <paramref name="count"/> human-readable one-time recovery codes (raw).</summary>
    public IReadOnlyList<string> GenerateRecoveryCodes(int count);
}

/// <summary>Encrypts/decrypts the TOTP secret at rest (Data Protection).</summary>
public interface IMfaSecretProtector
{
    public string Protect(string plaintext);

    public string Unprotect(string protectedValue);

    public bool TryUnprotect(string protectedValue, out string plaintext);
}
