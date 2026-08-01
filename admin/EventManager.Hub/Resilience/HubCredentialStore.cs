using System.Text;
using EventManager.Hub.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventManager.Hub.Resilience;

/// <summary>The credential in usable form. Only ever materialized in memory, at point of use.</summary>
public sealed record HubCloudCredential(string Key, string CloudBaseUrl, DateTimeOffset InstalledAt);

public enum CredentialInstallOutcome { Installed, RefusedSlotOccupied, RefusedInsecureUrl, RefusedInvalidUrl }

/// <summary>
/// Read side of credential custody. Exists so the singleton transport can obtain a credential without
/// capturing the scoped store that holds a <c>DbContext</c> — the same captive-dependency hazard
/// CL-1=A addresses for <see cref="ReplicationClient"/>.
/// </summary>
public interface IHubCredentialReader
{
    Task<HubCloudCredential?> TryGetAsync(CancellationToken ct = default);
}

/// <summary>
/// The hub's local custody of its cloud credential (US-802, BR-REPL-22..25).
///
/// Installing while a credential is present is REFUSED rather than overwriting (FD-Q8=B): the cost is
/// a two-step rotation, the benefit is that a working credential cannot be destroyed by a careless
/// paste. Clearing is a separate, explicit action.
///
/// No read path returns the key to a caller that only needs to know whether one exists.
/// </summary>
public sealed class HubCredentialStore(HubDbContext db, ISecretProtector protector, ReplicationOptions options)
    : IHubCredentialReader
{
    public async Task<CredentialInstallOutcome> InstallAsync(string key, string cloudBaseUrl, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(cloudBaseUrl, UriKind.Absolute, out var uri)) return CredentialInstallOutcome.RefusedInvalidUrl;

        // BR-REPL-26: a non-HTTPS base URL is refused unless the development override is explicit.
        var isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !options.AllowInsecureBaseUrl) return CredentialInstallOutcome.RefusedInsecureUrl;

        var existing = await db.HubCredential.FirstOrDefaultAsync(ct);
        if (existing is not null) return CredentialInstallOutcome.RefusedSlotOccupied;

        db.HubCredential.Add(new HubCredentialRow
        {
            Id = HubCredentialRow.SingletonId,
            ProtectedKey = protector.Protect(Encoding.UTF8.GetBytes(key)),
            CloudBaseUrl = cloudBaseUrl,
            InstalledAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return CredentialInstallOutcome.Installed;
    }

    /// <summary>
    /// Load for use. Returns null when nothing is installed, and also when the stored value cannot be
    /// unprotected — which happens cleanly if the hub now runs under a different Windows account than
    /// the one that installed it (see <see cref="DpapiSecretProtector"/>). Failing to null rather than
    /// throwing keeps that a "no usable credential" condition rather than a crash.
    /// </summary>
    public async Task<HubCloudCredential?> TryGetAsync(CancellationToken ct = default)
    {
        var row = await db.HubCredential.AsNoTracking().FirstOrDefaultAsync(ct);
        if (row is null) return null;

        try
        {
            var key = Encoding.UTF8.GetString(protector.Unprotect(row.ProtectedKey));
            return new HubCloudCredential(key, row.CloudBaseUrl, row.InstalledAt);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Whether a credential is installed — deliberately without returning it (BR-REPL-24).</summary>
    public Task<bool> ExistsAsync(CancellationToken ct = default) => db.HubCredential.AnyAsync(ct);

    public async Task ClearAsync(CancellationToken ct = default)
    {
        var row = await db.HubCredential.FirstOrDefaultAsync(ct);
        if (row is null) return;
        db.HubCredential.Remove(row);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Resolves a fresh <see cref="HubCredentialStore"/> per read, so a singleton consumer never holds a
/// scoped <c>DbContext</c>. Reading the credential is rare — once per replication attempt — so the
/// scope cost is irrelevant next to the correctness it buys.
/// </summary>
public sealed class ScopedHubCredentialReader(IServiceScopeFactory scopeFactory) : IHubCredentialReader
{
    public async Task<HubCloudCredential?> TryGetAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<HubCredentialStore>();
        return await store.TryGetAsync(ct);
    }
}
