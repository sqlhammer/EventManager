using EventManager.Sync;

namespace EventManager.Contracts;

/// <summary>Maps between the domain event (U1) and its wire envelope. Round-trips losslessly (BR-1.6).</summary>
public static class EventEnvelopeMapper
{
    public static EventEnvelopeDto ToDto(TournamentEvent e) => new(
        e.EventId,
        e.DeviceId,
        e.SequenceNumber,
        e.EventType,
        e.SchemaVersion,
        Convert.ToBase64String(e.Payload.Span),
        e.OccurredAt,
        e.EventScopeId);

    public static TournamentEvent ToEvent(EventEnvelopeDto d) => new(
        d.EventId,
        d.DeviceId,
        d.SequenceNumber,
        d.EventType,
        d.SchemaVersion,
        Convert.FromBase64String(d.PayloadBase64),
        d.OccurredAt,
        d.EventScopeId);
}
