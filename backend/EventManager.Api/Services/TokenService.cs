using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EventManager.Api.Persistence;
using Microsoft.IdentityModel.Tokens;

namespace EventManager.Api.Services;

/// <summary>JWT settings (SP-1/S7, Q2=A). Signing key + lifetimes injected from config/secrets (NFR-2.6).</summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "eventmanager";
    public string Audience { get; set; } = "eventmanager";
    public string SigningKey { get; set; } = "";      // injected; never committed
    public int AccessMinutes { get; set; } = 60;      // Q2=A
    public int RefreshDays { get; set; } = 14;        // Q2=A
}

public sealed record IssuedTokens(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt);

/// <summary>
/// Issues access tokens and rotating refresh tokens; logout/rotation revoke via the store
/// (BR-AUTH-6). Access carries the account id claim used for object-level authz.
/// </summary>
public sealed class TokenService(JwtOptions options, RefreshTokenStore refreshTokens)
{
    public const string AccountIdClaim = "acct";

    public async Task<IssuedTokens> IssueAsync(long accountId, string email, bool mfaSatisfied, CancellationToken ct = default)
    {
        var accessExpires = DateTimeOffset.UtcNow.AddMinutes(options.AccessMinutes);
        var creds = new SigningCredentials(Key(), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString()),
                new Claim(AccountIdClaim, accountId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim("mfa", MfaClaim(mfaSatisfied)),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            expires: accessExpires.UtcDateTime,
            signingCredentials: creds);

        var access = new JwtSecurityTokenHandler().WriteToken(jwt);
        var refresh = RandomToken();
        await refreshTokens.IssueAsync(refresh, accountId, DateTimeOffset.UtcNow.AddDays(options.RefreshDays), ct);
        return new IssuedTokens(access, refresh, accessExpires);
    }

    /// <summary>Rotate a refresh token: validate, revoke old, issue a new access + refresh (Q2=A).</summary>
    public async Task<IssuedTokens?> RefreshAsync(string refreshToken, string email, CancellationToken ct = default)
    {
        var accountId = await refreshTokens.ValidateAsync(refreshToken, ct);
        if (accountId is null) return null;

        var newRefresh = RandomToken();
        var ok = await refreshTokens.RotateAsync(refreshToken, newRefresh, DateTimeOffset.UtcNow.AddDays(options.RefreshDays), ct);
        if (!ok) return null;

        var accessExpires = DateTimeOffset.UtcNow.AddMinutes(options.AccessMinutes);
        var jwt = new JwtSecurityToken(options.Issuer, options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, accountId.Value.ToString()),
                new Claim(AccountIdClaim, accountId.Value.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim("mfa", "1"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            expires: accessExpires.UtcDateTime,
            signingCredentials: new SigningCredentials(Key(), SecurityAlgorithms.HmacSha256));
        var access = new JwtSecurityTokenHandler().WriteToken(jwt);
        return new IssuedTokens(access, newRefresh, accessExpires);
    }

    public Task LogoutAsync(long accountId, CancellationToken ct = default) =>
        refreshTokens.RevokeAllForAccountAsync(accountId, ct);

    private SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(options.SigningKey));

    private static string RandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string MfaClaim(bool mfaSatisfied)
    {
        if (mfaSatisfied) return "1";
        return "0";
    }
}
