using EventManager.Contracts;
using EventManager.Sync;

namespace EventManager.ClientSync;

/// <summary>
/// Applies hub push messages through U1's <see cref="ProjectionHost{TState}"/> (idempotent, BR-CS-4)
/// and raises a typed <see cref="Changed"/> event the app subscribes to (Q3=A, U2-TSD-3).
/// Push payloads carry an <see cref="EventEnvelopeDto"/> to apply.
/// </summary>
public sealed class HubPushConsumer<TState>
{
    private readonly ProjectionHost<TState> _projection;

    public HubPushConsumer(ProjectionHost<TState> projection) => _projection = projection;

    /// <summary>Raised after a push is applied. Argument is the push type.</summary>
    public event Action<PushType>? Changed;

    public TState State => _projection.State;

    public void OnPush(HubPushMessageDto message)
    {
        var envelope = System.Text.Json.JsonSerializer.Deserialize<EventEnvelopeDto>(
            Convert.FromBase64String(message.PayloadBase64));
        if (envelope is not null)
            _projection.Dispatch(EventEnvelopeMapper.ToEvent(envelope));

        Changed?.Invoke(message.PushType);
    }
}
