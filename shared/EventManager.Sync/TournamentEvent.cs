namespace EventManager.Sync;

/// <summary>
/// The immutable atom of state (FR-4.2, D-26). Identity = <see cref="EventId"/> (Snowflake:
/// PK, idempotence key, canonical sort key). <see cref="DeviceId"/> + <see cref="SequenceNumber"/>
/// give a per-device contiguous stream for gap-free replication (Q9).
/// Payload is opaque at the Sync layer (serialized bytes); consuming units interpret it.
/// </summary>
public sealed record TournamentEvent(
    long EventId,
    long DeviceId,
    long SequenceNumber,
    string EventType,
    int SchemaVersion,
    ReadOnlyMemory<byte> Payload,
    DateTimeOffset OccurredAt,
    long EventScopeId)
{
    public bool Equals(TournamentEvent? other) => other is not null && other.EventId == EventId;
    public override int GetHashCode() => EventId.GetHashCode();
}
