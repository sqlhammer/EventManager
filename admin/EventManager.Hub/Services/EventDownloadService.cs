using EventManager.Hub.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Hub.Services;

/// <summary>
/// Event-download readiness (US-301). The actual pull of the full event stream + role assignments +
/// worker-id reservations from the cloud is a seam (the MAUI host / U7 wire the cloud client); this
/// service owns the readiness gate — once marked, the hub is "event-day ready, internet not required".
/// </summary>
public sealed class EventDownloadService(HubDbContext db)
{
    public async Task MarkReadyAsync(long eventId, CancellationToken ct = default)
    {
        var row = await db.Readiness.FindAsync([eventId], ct);
        if (row is null)
            db.Readiness.Add(new ReadinessRecord { EventId = eventId, Ready = true, DownloadedAt = DateTimeOffset.UtcNow });
        else { row.Ready = true; row.DownloadedAt = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> IsReadyAsync(long eventId, CancellationToken ct = default) =>
        await db.Readiness.AsNoTracking().AnyAsync(r => r.EventId == eventId && r.Ready, ct);
}
