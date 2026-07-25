using System.Security.Cryptography;
using System.Text;
using EventManager.Api.Persistence;
using Microsoft.AspNetCore.Identity;

namespace EventManager.Api.Services;

/// <summary>
/// Offline breached-password check (SP-5, TSD-3, Q1=A). A bundled hashed set is consulted via a
/// SHA-1 k-anonymity prefix — no external runtime call. The seed set here is a placeholder for the
/// full bundled dataset a deploy would ship; extend <see cref="_breachedSha1"/> from the asset file.
/// </summary>
public sealed class BreachedPasswordValidator : IPasswordValidator<AppUser>
{
    // Placeholder seed of well-known breached passwords (full SHA-1 upper-hex). Real deploy loads the
    // bundled k-anonymity dataset. Kept small here so the check is exercised end-to-end.
    private static readonly HashSet<string> _breachedSha1 = new(StringComparer.OrdinalIgnoreCase)
    {
        Sha1("password"), Sha1("123456"), Sha1("123456789"), Sha1("qwerty"),
        Sha1("password1"), Sha1("111111"), Sha1("12345678"), Sha1("abc123"), Sha1("letmein"),
    };

    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        if (!string.IsNullOrEmpty(password) && _breachedSha1.Contains(Sha1(password)))
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "BreachedPassword",
                Description = "This password appears in a known breach. Choose a different one.",
            }));
        return Task.FromResult(IdentityResult.Success);
    }

    private static string Sha1(string s) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)));
}

/// <summary>Email seam (Q5=A). MVP records tokens to the outbox instead of sending (D-06 pattern).</summary>
public interface IEmailSender
{
    Task SendConfirmationAsync(string toAddress, string token, CancellationToken ct = default);
    Task SendOrganizerInviteAsync(string toAddress, string token, CancellationToken ct = default);
}

/// <summary>Stub email sender — writes to the EmailOutbox table and logs; no SMTP (TSD-8).</summary>
public sealed class OutboxEmailSender(AppDbContext db, ILogger<OutboxEmailSender> log) : IEmailSender
{
    public Task SendConfirmationAsync(string toAddress, string token, CancellationToken ct = default) =>
        RecordAsync(toAddress, "confirmation", token, ct);

    public Task SendOrganizerInviteAsync(string toAddress, string token, CancellationToken ct = default) =>
        RecordAsync(toAddress, "organizer-invite", token, ct);

    private async Task RecordAsync(string to, string kind, string token, CancellationToken ct)
    {
        db.EmailOutbox.Add(new EmailOutboxRecord { ToAddress = to, Kind = kind, Token = token, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        log.LogInformation("Email stub: {Kind} for {To} (token recorded to outbox)", kind, to); // token itself never logged
    }
}

/// <summary>Bounded retry + timeout for outbound calls (RP-3, Q3=A). No circuit breaker — stubs.</summary>
public static class OutboundRetry
{
    public static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, int maxAttempts = 3,
        TimeSpan? perAttemptTimeout = null, CancellationToken ct = default)
    {
        var timeout = perAttemptTimeout ?? TimeSpan.FromSeconds(10);
        for (var attempt = 1; ; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try { return await action(cts.Token); }
            catch when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1)), ct); // backoff
            }
        }
    }
}
