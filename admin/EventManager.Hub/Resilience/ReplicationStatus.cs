namespace EventManager.Hub.Resilience;

/// <summary>
/// What an organizer can see about replication (US-806). Every field is computed in-process, so the
/// one question asked during an outage is answerable during an outage (BR-REPL-48).
/// </summary>
public sealed record ReplicationStatusSnapshot(
    bool CredentialInstalled,
    DateTimeOffset? LastSuccessAt,
    long PendingEvents,
    double? LagSeconds,
    int ConsecutiveFailures,
    string CircuitState,
    string? LastPermanentFailure,
    bool AsOfLastRun);

/// <summary>
/// Live replication health, updated by the client and transport (AD-Q7=A).
///
/// Values here are cached from the last replication run and are served to <c>/health</c> and to the
/// metrics exporter, which are hit frequently (BR-REPL-47). The human-facing status route computes
/// pending and lag on demand instead — see <c>ReplicationClient.ComputeStatusAsync</c>.
/// </summary>
public sealed class ReplicationStatus
{
    private readonly Lock _gate = new();

    public DateTimeOffset? LastSuccessAt { get; private set; }
    public long PendingEvents { get; private set; }
    public double? LagSeconds { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public CircuitState Circuit { get; private set; } = CircuitState.Closed;
    public bool CredentialInstalled { get; set; }

    /// <summary>The most recent failure that will not resolve itself — an operator must act on it.</summary>
    public string? LastPermanentFailure { get; private set; }

    public void RecordSuccess(DateTimeOffset at, long pending, double? lagSeconds)
    {
        lock (_gate)
        {
            LastSuccessAt = at;
            PendingEvents = pending;
            LagSeconds = lagSeconds;
            ConsecutiveFailures = 0;
            LastPermanentFailure = null;
        }
    }

    public void RecordFailure(ReplicationFailure failure, int consecutiveFailures)
    {
        lock (_gate)
        {
            ConsecutiveFailures = consecutiveFailures;
            if (failure.Kind == FailureKind.Permanent) LastPermanentFailure = failure.Reason;
        }
    }

    public void RecordCircuit(CircuitState state)
    {
        lock (_gate) Circuit = state;
    }

    /// <summary>The cached view — cheap, and safe to serve on every health probe.</summary>
    public ReplicationStatusSnapshot Snapshot()
    {
        lock (_gate)
            return new ReplicationStatusSnapshot(CredentialInstalled, LastSuccessAt, PendingEvents, LagSeconds,
                ConsecutiveFailures, Circuit.ToString(), LastPermanentFailure, AsOfLastRun: true);
    }
}
