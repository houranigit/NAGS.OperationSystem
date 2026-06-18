using System.Security.Cryptography;
using Identity.Application.Abstractions;

namespace Identity.Infrastructure.Services;

public sealed class InvitationTokenGenerator : IInvitationTokenGenerator
{
    public TimeSpan ExpiryDuration => TimeSpan.FromDays(7);

    public string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
