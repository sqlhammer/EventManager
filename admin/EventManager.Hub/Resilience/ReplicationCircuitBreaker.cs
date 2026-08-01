namespace EventManager.Hub.Resilience;

public enum CircuitState { Closed, Open, HalfOpen }

/// <summary>
/// Stops the hub hammering a dead link (BR-REPL-34..36). Opens after N consecutive CONNECTION
/// failures; after a cool-down it permits exactly one trial request; success closes and resets,
/// failure re-opens.
///
/// Only connection failures advance it. A server-side failure means the cloud is reachable and
/// unwell — opening the breaker there would suppress retries that would have succeeded.
///
/// An open breaker makes replication a no-op, never an error: nothing is surfaced to the people
/// running the event.
/// </summary>
public sealed class ReplicationCircuitBreaker(ReplicationOptions options, TimeProvider clock)
{
    private readonly Lock _gate = new();
    private int _consecutiveFailures;
    private DateTimeOffset? _openedAt;
    private bool _trialInFlight;

    public CircuitState State
    {
        get
        {
            lock (_gate) return StateAt(clock.GetUtcNow());
        }
    }

    public int ConsecutiveFailures
    {
        get
        {
            lock (_gate) return _consecutiveFailures;
        }
    }

    /// <summary>
    /// Whether an attempt may proceed. In the half-open state exactly one trial is allowed through;
    /// concurrent callers are refused until it reports back.
    /// </summary>
    public bool TryAcquire()
    {
        lock (_gate)
        {
            var state = StateAt(clock.GetUtcNow());
            if (state == CircuitState.Closed) return true;
            if (state == CircuitState.Open) return false;

            if (_trialInFlight) return false;
            _trialInFlight = true;
            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _openedAt = null;
            _trialInFlight = false;
        }
    }

    /// <summary>Only call for a connection failure — see <see cref="ReplicationFailure.AdvancesBreaker"/>.</summary>
    public void RecordConnectionFailure()
    {
        lock (_gate)
        {
            _trialInFlight = false;
            _consecutiveFailures++;
            if (_consecutiveFailures >= options.BreakerFailureThreshold) _openedAt = clock.GetUtcNow();
        }
    }

    /// <summary>A non-connection failure leaves the breaker where it is (BR-REPL-34).</summary>
    public void RecordNonConnectionFailure()
    {
        lock (_gate) _trialInFlight = false;
    }

    private CircuitState StateAt(DateTimeOffset now)
    {
        if (_openedAt is null) return CircuitState.Closed;
        if (now - _openedAt.Value >= options.BreakerCooldown) return CircuitState.HalfOpen;
        return CircuitState.Open;
    }
}
