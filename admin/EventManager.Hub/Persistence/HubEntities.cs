namespace EventManager.Hub.Persistence;

/// <summary>Hub-local event-log row (SQLite). SQLCipher at-rest encryption is a deferred seam (D-09).</summary>
public sealed class HubEventRecord
{
    public long EventId { get; set; }
    public long DeviceId { get; set; }
    public long SequenceNumber { get; set; }
    public string EventType { get; set; } = "";
    public int SchemaVersion { get; set; }
    public byte[] Payload { get; set; } = [];
    public DateTimeOffset OccurredAt { get; set; }
    public long EventScopeId { get; set; }
}

/// <summary>Device credential projection (US-303/305/508). Revoked credentials are rejected on next contact.</summary>
public sealed class DeviceRecord
{
    public long DeviceId { get; set; }
    public long EventId { get; set; }       // the tournament event scope
    public string RoleDescriptor { get; set; } = "";  // e.g. "Judge — Mat 2"
    public int WorkerId { get; set; }
    public bool Revoked { get; set; }
}

/// <summary>One-time pairing token (US-303/304). Single-use: consumed on redemption.</summary>
public sealed class PairingTokenRecord
{
    public string Token { get; set; } = "";   // PK
    public long EventId { get; set; }
    public string RoleDescriptor { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Consumed { get; set; }
}

/// <summary>Offline organizer credential packaged at event download (D-27). Enables hub-side auth
/// without internet; role assignments drive the reused U1 RoleAuthorizationPolicy.</summary>
public sealed class OrganizerCredentialRecord
{
    public long AccountId { get; set; }        // PK component
    public long EventId { get; set; }
    public string Role { get; set; } = "";     // FullAdmin | CoOrganizer
    public string PasswordHash { get; set; } = "";  // packaged hash for offline verification
}

/// <summary>Event-download readiness flag (US-301): the hub is "event-day ready" once set.</summary>
public sealed class ReadinessRecord
{
    public long EventId { get; set; }          // PK
    public bool Ready { get; set; }
    public DateTimeOffset DownloadedAt { get; set; }
}
