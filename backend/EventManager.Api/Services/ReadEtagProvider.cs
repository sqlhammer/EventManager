using System.Security.Cryptography;
using System.Text;
using EventManager.Api.Auth;
using EventManager.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Services;

/// <summary>
/// Watermark-based ETags for event-scoped reads (U9-NFR-1, C3=D).
///
/// The watermark is MAX(EventRecord.EventId) for the event scope — EventId is a monotonic Snowflake
/// and EventScopeId is indexed, so this is a single cheap lookup that acts as a true version token.
/// It is EXACT rather than approximate because CloudProjectionHost folds read models in the same
/// transaction and DbContext as the append (EventWriter.AppendAsync), so there is no projection lag.
///
/// ⚠ U9-CON-3: that exactness depends on projection staying SYNCHRONOUS AND INLINE. If projections
/// ever become asynchronous, this watermark will run ahead of the read models and conditional
/// requests will start serving stale data. Switch to a projection-applied high-water mark if that
/// changes — do not simply leave this in place.
/// </summary>
public sealed class ReadEtagProvider(AppDbContext db)
{
    /// <summary>Latest event-log position for the scope; null when the scope has no events yet.</summary>
    public async Task<long?> WatermarkAsync(long eventScopeId, CancellationToken ct = default)
    {
        var any = await db.Events.AsNoTracking().AnyAsync(e => e.EventScopeId == eventScopeId, ct);
        if (!any) return null;
        return await db.Events.AsNoTracking().Where(e => e.EventScopeId == eventScopeId).MaxAsync(e => e.EventId, ct);
    }

    /// <summary>
    /// Build the opaque ETag. The token covers EVERY input that determines the body — not just the
    /// watermark (BR-READ-22).
    ///
    /// Why that matters: the same event at the same watermark renders differently per tier and per
    /// inclusion flag. A watermark-only ETag would let a caller who gained a tier (say, by
    /// registering for the event) present their old If-None-Match and receive a 304 saying "nothing
    /// changed" while still holding the narrower Public body — silently withholding data they are
    /// now entitled to.
    ///
    /// The token is a hash, never the raw watermark: a bare Snowflake would leak event-log volume
    /// and last-activity timing to any caller holding the public tier (BR-READ-23).
    /// </summary>
    public string Build(string endpointIdentity, long eventId, long? watermark, AccessTier tier, params string[] flags)
    {
        var material = new StringBuilder();
        material.Append(endpointIdentity).Append('|')
                .Append(eventId).Append('|')
                .Append(watermark?.ToString() ?? "none").Append('|')
                .Append((int)tier);
        foreach (var flag in flags) material.Append('|').Append(flag);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString()));
        return "\"" + Convert.ToHexString(hash)[..32].ToLowerInvariant() + "\"";
    }

    /// <summary>True when the request's If-None-Match matches the computed tag, so a 304 is correct.</summary>
    public static bool Matches(string? ifNoneMatchHeader, string etag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatchHeader)) return false;
        foreach (var candidate in ifNoneMatchHeader.Split(','))
        {
            var trimmed = candidate.Trim();
            if (trimmed == "*") return true;
            if (trimmed.StartsWith("W/", StringComparison.Ordinal)) trimmed = trimmed[2..];
            if (string.Equals(trimmed, etag, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
