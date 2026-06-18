namespace Identity.Application.Abstractions;

public interface ITokenService
{
    string GenerateAccessToken(
        Guid userId,
        string email,
        string username,
        string userType,
        IReadOnlyList<string> permissions);

    string GenerateRefreshToken();

    TimeSpan AccessTokenExpiry { get; }
    TimeSpan RefreshTokenExpiry { get; }
}
