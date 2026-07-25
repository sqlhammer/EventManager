using EventManager.Sync;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;

namespace EventManager.Api.Persistence;

/// <summary>
/// Cloud Npgsql adapter for the shared <see cref="IEventStore"/> (TSD-1). Idempotent append via a
/// unique (DeviceId, SequenceNumber) constraint — replays/retries never duplicate (RP-1, BR-ING-2).
/// Single-writer contract honored by the DB constraint, so it stays horizontal-scale-ready (SC-1).
/// </summary>
public sealed class PostgresEventStore(AppDbContext db) : IEventStore
{
    public async Task<bool> AppendIfNotExistsAsync(TournamentEvent evt, CancellationToken ct = default)
    {
        var exists = await db.Events.AnyAsync(
            e => e.DeviceId == evt.DeviceId && e.SequenceNumber == evt.SequenceNumber, ct);
        if (exists) return false;

        db.Events.Add(new EventRecord
        {
            EventId = evt.EventId,
            DeviceId = evt.DeviceId,
            SequenceNumber = evt.SequenceNumber,
            EventType = evt.EventType,
            SchemaVersion = evt.SchemaVersion,
            Payload = evt.Payload.ToArray(),
            OccurredAt = evt.OccurredAt,
            EventScopeId = evt.EventScopeId,
        });
        // SaveChanges is owned by the caller's transaction (write path). A duplicate racing insert
        // still fails on the unique index, preserving idempotence under concurrency.
        return true;
    }

    public async Task<IReadOnlyList<TournamentEvent>> ReadStreamAsync(long deviceId, long fromSequenceExclusive, CancellationToken ct = default)
    {
        var rows = await db.Events.AsNoTracking()
            .Where(e => e.DeviceId == deviceId && e.SequenceNumber > fromSequenceExclusive)
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<long> HighWaterMarkAsync(long deviceId, CancellationToken ct = default)
    {
        // Gap-free contiguous high-water mark (BR-1.5). Single-writer cloud stream is contiguous.
        var seqs = await db.Events.AsNoTracking()
            .Where(e => e.DeviceId == deviceId)
            .OrderBy(e => e.SequenceNumber)
            .Select(e => e.SequenceNumber)
            .ToListAsync(ct);
        long hwm = 0;
        foreach (var s in seqs)
        {
            if (s == hwm + 1) hwm = s;
            else if (s > hwm + 1) break;
        }
        return hwm;
    }

    public async IAsyncEnumerable<TournamentEvent> ReadAllAsync(long? fromEventIdExclusive = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var q = db.Events.AsNoTracking().OrderBy(e => e.EventId).AsQueryable();
        if (fromEventIdExclusive is { } from) q = q.Where(e => e.EventId > from).OrderBy(e => e.EventId);
        await foreach (var row in q.AsAsyncEnumerable().WithCancellation(ct))
            yield return Map(row);
    }

    public async Task<IReadOnlyList<long>> ListDeviceIdsAsync(CancellationToken ct = default) =>
        await db.Events.AsNoTracking().Select(e => e.DeviceId).Distinct().ToListAsync(ct);

    private static TournamentEvent Map(EventRecord r) => new(
        r.EventId, r.DeviceId, r.SequenceNumber, r.EventType, r.SchemaVersion,
        r.Payload, r.OccurredAt, r.EventScopeId);
}
