using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace EventManager.Api.Auth;

/// <summary>
/// Ingest hardening constants and partitioning (U10-FR-15, ND-Q1=C, ND-Q2=B, P-4/P-5/P-8).
///
/// Partitioning is constrained by pipeline order: <c>UseRateLimiter()</c> runs BEFORE
/// <c>UseAuthentication()</c>, so no principal exists yet and a policy cannot partition by credential
/// id. It therefore partitions by a HASH of the presented credential header — available pre-auth with
/// no database lookup, and giving true per-hub isolation. Client IP was rejected because venue hubs
/// sit behind NAT, which for this product is the normal case rather than an edge case.
///
/// The raw header value is never used as a partition key and never logged — only its hash.
/// </summary>
public static class IngestPolicy
{
    public const string Name = "ingest";

    /// <summary>Path the bulkhead applies to. The ingest routes are the only ones a hub calls.</summary>
    public const string PathPrefix = "/api/ingest";

    /// <summary>Server-side body cap: twice the hub's 4 MB batch cap, so a conforming hub never trips it.</summary>
    public const long MaxRequestBytes = 8 * 1024 * 1024;

    public const int PermitLimit = 300;                       // requests per window, per partition
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Global cap on simultaneous in-flight ingest requests — the bulkhead (P-4).</summary>
    public const int MaxConcurrency = 8;
    public const int ConcurrencyQueueLimit = 32;

    private const string AnonymousPartition = "anonymous";

    /// <summary>
    /// Partition key for a request. Requests with no credential header share a single bucket, so an
    /// unauthenticated flood cannot consume per-hub capacity.
    /// </summary>
    public static string PartitionKey(HttpContext ctx)
    {
        if (!ctx.Request.Headers.TryGetValue(HubCredentialDefaults.HeaderName, out var presented))
            return AnonymousPartition;

        var raw = presented.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return AnonymousPartition;

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    public static RateLimitPartition<string> Partition(HttpContext ctx) =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(ctx),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = PermitLimit, Window = Window });

    /// <summary>
    /// The bulkhead (P-4): a single global concurrency cap across all ingest traffic. Everything
    /// outside <see cref="PathPrefix"/> is unlimited, so this changes nothing for existing routes.
    /// </summary>
    public static RateLimitPartition<string> GlobalPartition(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments(PathPrefix)) return RateLimitPartition.GetNoLimiter("unlimited");

        return RateLimitPartition.GetConcurrencyLimiter("ingest-bulkhead",
            _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = MaxConcurrency,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = ConcurrencyQueueLimit,
            });
    }
}
