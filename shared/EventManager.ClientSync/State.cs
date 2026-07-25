namespace EventManager.ClientSync;

public enum ConnectionState { Disconnected, Connecting, Connected }

public enum QueueState { Pending, Sent, Acked }

/// <summary>Immutable, thread-safe snapshot of sync status surfaced to the UI (BR-CS-7).</summary>
public sealed record SyncStatus(
    ConnectionState Connection,
    int QueuedCount,
    long LastAckedSequence,
    DateTimeOffset? LastSyncAt);

/// <summary>Device credential obtained at pairing; used on every connection.</summary>
public sealed record DeviceCredentialRef(long DeviceId, int WorkerId, string RoleDescriptor, string HubCertFingerprint);

/// <summary>Bounded exponential backoff (U2-TSD-7).</summary>
public sealed record BackoffPolicy(TimeSpan InitialDelay, TimeSpan MaxDelay, double Multiplier)
{
    public static readonly BackoffPolicy Default =
        new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), 2.0);

    public TimeSpan DelayForAttempt(int attempt)
    {
        var ms = InitialDelay.TotalMilliseconds * Math.Pow(Multiplier, Math.Max(0, attempt));
        return TimeSpan.FromMilliseconds(Math.Min(ms, MaxDelay.TotalMilliseconds));
    }
}
