using System.Security.Claims;
using System.Text.Encodings.Web;
using EventManager.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EventManager.Api.Auth;

/// <summary>A hub authenticated by its own credential — not a person (AD-Q2=A, AD-Q3=A).</summary>
public sealed record HubCredentialPrincipal(long CredentialId, long EventScopeId);

/// <summary>
/// The principal that presented a batch (E-3). Deliberately a closed set of two: a hub is not a
/// person and is not modelled as one. Mapping a credential onto its issuing account would attribute
/// hub writes to someone who was not present and let its reach follow that organizer's role changes.
/// </summary>
public abstract record IngestCaller
{
    public sealed record Account(long AccountId) : IngestCaller;
    public sealed record Hub(long CredentialId, long EventScopeId) : IngestCaller;
}

/// <summary>Constants for the hub-credential authentication scheme.</summary>
public static class HubCredentialDefaults
{
    public const string Scheme = "HubCredential";
    public const string HeaderName = "X-Hub-Credential";
    public const string CredentialIdClaim = "hub_cred";
    public const string EventScopeClaim = "hub_scope";
}

/// <summary>
/// Authenticates a hub by the key in <see cref="HubCredentialDefaults.HeaderName"/> (BR-REPL-7..9).
/// Every failure — absent, unknown, expired, revoked — produces one indistinguishable result, so the
/// endpoint cannot be used to probe which credentials exist (SECURITY-09). The key is never logged.
/// </summary>
public sealed class HubCredentialAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    HubCredentialService credentials)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HubCredentialDefaults.HeaderName, out var presented))
            return AuthenticateResult.NoResult();

        var principal = await credentials.AuthenticateAsync(presented.ToString(), Context.RequestAborted);
        if (principal is null) return AuthenticateResult.Fail("Invalid credential.");   // deliberately uninformative

        var identity = new ClaimsIdentity(
        [
            new Claim(HubCredentialDefaults.CredentialIdClaim, principal.CredentialId.ToString()),
            new Claim(HubCredentialDefaults.EventScopeClaim, principal.EventScopeId.ToString()),
        ], HubCredentialDefaults.Scheme);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), HubCredentialDefaults.Scheme);
        return AuthenticateResult.Success(ticket);
    }
}

/// <summary>
/// Resolves the request's <see cref="IngestCaller"/>. A hub credential wins when present, because a
/// route that accepts both schemes should treat a presented hub credential as the caller's intent.
/// </summary>
public sealed class CurrentCaller(IHttpContextAccessor accessor)
{
    public IngestCaller? Resolve()
    {
        var user = accessor.HttpContext?.User;
        if (user is null) return null;

        var credRaw = user.FindFirstValue(HubCredentialDefaults.CredentialIdClaim);
        var scopeRaw = user.FindFirstValue(HubCredentialDefaults.EventScopeClaim);
        if (long.TryParse(credRaw, out var credentialId) && long.TryParse(scopeRaw, out var scopeId))
            return new IngestCaller.Hub(credentialId, scopeId);

        var acctRaw = user.FindFirstValue(TokenService.AccountIdClaim);
        if (long.TryParse(acctRaw, out var accountId)) return new IngestCaller.Account(accountId);

        return null;
    }

    public IngestCaller Require() => Resolve() ?? throw new UnauthorizedAccessException("No authenticated caller.");
}
