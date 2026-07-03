using System.Net;
using System.Security.Cryptography;
using Identity.Application;
using Identity.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Security;

/// <summary>TOTP + recovery-code generation built on the dependency-free <see cref="Totp"/> helper.</summary>
public sealed class MfaService(IOptions<IdentityModuleOptions> options) : IMfaService
{
    private readonly IdentityModuleOptions _options = options.Value;

    public string GenerateSecret() => Totp.GenerateSecret();

    public string BuildOtpAuthUri(string secret, string accountName)
    {
        var issuer = WebUtility.UrlEncode(_options.Jwt.Issuer);
        var label = WebUtility.UrlEncode(accountName);
        return $"otpauth://totp/{issuer}:{label}?secret={secret}&issuer={issuer}&digits=6&period=30";
    }

    public bool VerifyCode(string secret, string code, DateTimeOffset now) => Totp.Verify(secret, code, now);

    public IReadOnlyList<string> GenerateRecoveryCodes(int count)
    {
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            // 10 hex chars in two groups, e.g. "3F9A1-C72B0".
            var bytes = RandomNumberGenerator.GetBytes(5);
            var hex = Convert.ToHexString(bytes);
            codes.Add($"{hex[..5]}-{hex[5..]}");
        }

        return codes;
    }
}

/// <summary>Data Protection-backed encryption of the TOTP secret at rest.</summary>
public sealed class DataProtectionMfaSecretProtector : IMfaSecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionMfaSecretProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("OperationsSystem.Identity.MfaSecret.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);

    public bool TryUnprotect(string protectedValue, out string plaintext)
    {
        try
        {
            plaintext = Unprotect(protectedValue);
            return true;
        }
        catch (CryptographicException)
        {
            plaintext = string.Empty;
            return false;
        }
    }
}
