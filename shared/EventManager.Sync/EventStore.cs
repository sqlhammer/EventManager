namespace EventManager.Sync;

/// <summary>
/// Append-only event log abstraction (Q2=B). Shared interface + shared replay/projection logic;
/// thin persistence adapters (SQLite in the hub, Npgsql in the cloud) implement this in U3/U4a.
/// Single-writer contract (P-7): consumers serialize writes.
/// </summary>
public interface IEventStore
{
    /// <summary>Idempotent append; returns false if an event with the same EventId already exists (BR-1.2).</summary>
    Task<bool> AppendIfNotExistsAsync(TournamentEvent evt, CancellationToken ct = default);

    /// <summary>Events for a device with SequenceNumber &gt; <paramref name="fromSequenceExclusive"/>, in sequence order.</summary>
    Task<IReadOnlyList<TournamentEvent>> ReadStreamAsync(long deviceId, long fromSequenceExclusive, CancellationToken ct = default);

    /// <summary>Last gap-free contiguous sequence for a device (BR-1.5).</summary>
    Task<long> HighWaterMarkAsync(long deviceId, CancellationToken ct = default);

    /// <summary>All events ordered by EventId (canonical fold order, Q7), optionally after an EventId.</summary>
    IAsyncEnumerable<TournamentEvent> ReadAllAsync(long? fromEventIdExclusive = null, CancellationToken ct = default);

    /// <summary>Distinct device ids present in the log.</summary>
    Task<IReadOnlyList<long>> ListDeviceIdsAsync(CancellationToken ct = default);
}
