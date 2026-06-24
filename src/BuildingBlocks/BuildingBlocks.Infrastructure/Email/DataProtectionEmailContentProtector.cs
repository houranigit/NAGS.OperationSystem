using BuildingBlocks.Application.Email;
using Microsoft.AspNetCore.DataProtection;

namespace BuildingBlocks.Infrastructure.Email;

/// <summary>Data Protection-backed <see cref="IEmailContentProtector"/>.</summary>
public sealed class DataProtectionEmailContentProtector : IEmailContentProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionEmailContentProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("OperationsSystem.Email.Body.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
