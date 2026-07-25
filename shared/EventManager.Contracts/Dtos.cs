namespace EventManager.Contracts;

/// <summary>Wire form of a TournamentEvent (payload carried as base64).</summary>
public sealed record EventEnvelopeDto(
    long EventId,
    long DeviceId,
    long SequenceNumber,
    string EventType,
    int SchemaVersion,
    string PayloadBase64,
    DateTimeOffset OccurredAt,
    long EventScopeId);

/// <summary>A batch of events for replication (spoke→hub, hub→cloud).</summary>
public sealed record ReplicationBatchDto(IReadOnlyList<EventEnvelopeDto> Events);

/// <summary>Ack with per-device high-water marks so the sender can advance its cursor.</summary>
public sealed record ReplicationAckDto(int AcceptedCount, IReadOnlyDictionary<long, long> PerDeviceHighWaterMarks);

/// <summary>Spoke enrollment request (one-time token).</summary>
public sealed record PairingRequestDto(string EnrollmentToken, string DevicePublicInfo);

/// <summary>Hub response granting a device credential + Snowflake worker id + pinned cert.</summary>
public sealed record PairingResponseDto(long DeviceId, int WorkerId, string RoleDescriptor, string HubCertFingerprint);

public enum PushType { BracketUpdated, ScheduleChanged, ResultsUpdated, DeviceRevoked }

/// <summary>Hub→spoke push envelope (SignalR).</summary>
public sealed record HubPushMessageDto(PushType PushType, string PayloadBase64);

/// <summary>Hub location for discovery (mDNS / manual IP / QR).</summary>
public sealed record HubDiscoveryInfoDto(string HubAddress, int Port, string CertFingerprint);
