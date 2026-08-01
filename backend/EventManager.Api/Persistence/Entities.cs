using EventManager.Domain;

namespace EventManager.Api.Persistence;

// ---------------------------------------------------------------------------
// Event-log row (domain plane, Q1=C). Mirrors Sync.TournamentEvent; the append-only
// source of truth. Idempotence: unique (DeviceId, SequenceNumber) + PK EventId.
// ---------------------------------------------------------------------------
public sealed class EventRecord
{
    public long EventId { get; set; }          // PK, Snowflake, canonical sort key
    public long DeviceId { get; set; }         // origin device (cloud worker for U3-authored)
    public long SequenceNumber { get; set; }   // per-device contiguous
    public string EventType { get; set; } = "";
    public int SchemaVersion { get; set; }
    public byte[] Payload { get; set; } = [];
    public DateTimeOffset OccurredAt { get; set; }
    public long EventScopeId { get; set; }     // tournament event id — partition + ingest authz key

    // Ingest provenance (U10, FD-Q7=B / BR-REPL-19..21). Nullable because cloud-authored events have
    // no delivering hub, and every pre-U10 row predates the column. Set once at insert, ingest path
    // only; duplicates are skipped rather than updated, so this is the FIRST deliverer (BR-REPL-20).
    public long? IngestedByCredentialId { get; set; }
}

// ---------------------------------------------------------------------------
// Hub credential (U10). The cloud's record of one hub's identity for one event.
// Only a hash of the key is stored (BR-REPL-3) — a reader of this table cannot
// recover a usable credential. State is DERIVED from the timestamps, never stored,
// so it cannot drift.
// ---------------------------------------------------------------------------
public sealed class HubCredentialRecord
{
    public long CredentialId { get; set; }       // PK, Snowflake
    public long EventScopeId { get; set; }       // the single event this credential may act on
    public string KeyHash { get; set; } = "";    // SHA-256 hex of the key (see HubCredentialService)
    public string Label { get; set; } = "";      // human identification only; carries no authority
    public long IssuedByAccountId { get; set; }  // audit, not authorization
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsRevoked => RevokedAt is not null;
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Active = neither revoked nor expired (BR-REPL-5 counts only these against the cap).</summary>
    public bool IsActive(DateTimeOffset now)
    {
        if (IsRevoked) return false;
        if (IsExpired(now)) return false;
        return true;
    }
}

// ---------------------------------------------------------------------------
// Read-model rows (projections fold events into these). Not authoritative.
// ---------------------------------------------------------------------------

public enum RegistrationStatusRow { Draft, Open, Closed }

public sealed class EventRow
{
    public long EventId { get; set; }          // = tournament event scope id
    public string Name { get; set; } = "";
    public string Venue { get; set; } = "";
    public DateOnly Date { get; set; }
    public DateOnly RegistrationStart { get; set; }
    public DateOnly RegistrationEnd { get; set; }
    public decimal EntryFee { get; set; }
    public RegistrationStatusRow RegistrationStatus { get; set; } = RegistrationStatusRow.Draft;
    public bool CardEnabled { get; set; }      // pay-at-door always on; card only when provider configured
    public long CreatedByAccountId { get; set; }
    public string WeighInPolicyMode { get; set; } = nameof(EventManager.Domain.WeighInPolicyMode.Strict);
    public double? WeighInTolerancePercent { get; set; }
    public bool CheckInStarted { get; set; }   // locks weigh-in policy (BR-EVT-5)
}

public sealed class DivisionRow
{
    public long DivisionId { get; set; }
    public long EventId { get; set; }
    public double? WeightLower { get; set; }
    public double WeightUpper { get; set; }
    public int MinRank { get; set; }
    public int MaxRank { get; set; }
    public int MinAge { get; set; }
    public int MaxAge { get; set; }
    public string Gender { get; set; } = "";
    public string Format { get; set; } = nameof(BracketFormat.SingleElimination);
    public string Status { get; set; } = nameof(DivisionStatus.NotStarted);
}

public sealed class OrganizerRow
{
    public long Id { get; set; }
    public long EventId { get; set; }
    public long AccountId { get; set; }
    public string Role { get; set; } = nameof(OrganizerRole.CoOrganizer);
}

public sealed class RegistrationRow
{
    public long RegistrationId { get; set; }
    public long EventId { get; set; }
    public long AthleteId { get; set; }
    public long ManagedByAccountId { get; set; }
    public string AthleteName { get; set; } = "";       // snapshot at submit time (BR-REG-2)
    public string Academy { get; set; } = "";
    public string DivisionIdsCsv { get; set; } = "";    // assigned divisions
    public string PaymentStatus { get; set; } = nameof(Domain.PaymentStatus.Owed);
    public bool HasAssignmentMismatch { get; set; }
    public string? MismatchReasons { get; set; }
    public bool Withdrawn { get; set; }
}

public sealed class AthleteProfileRow
{
    public long AthleteId { get; set; }
    public long OwnerAccountId { get; set; }
    public string Name { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public int Rank { get; set; }
    public double Weight { get; set; }
    public string Academy { get; set; } = "";
    public string Gender { get; set; } = "";
}

public sealed class ResultRow
{
    public long Id { get; set; }
    public long AthleteId { get; set; }
    public long EventId { get; set; }
    public long DivisionId { get; set; }
    public int? Placement { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public string Status { get; set; } = "";
}

// ---------------------------------------------------------------------------
// Infra tables
// ---------------------------------------------------------------------------

/// <summary>Command idempotency (RP-2, BR-REG-7, BR-PAY-1). key -> recorded first result.</summary>
public sealed class IdempotencyKey
{
    public string Key { get; set; } = "";      // PK
    public string ResultHash { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Rotating refresh token with revocation (SP-1, BR-AUTH-6).</summary>
public sealed class RefreshTokenRecord
{
    public string TokenHash { get; set; } = "";  // PK (hash, never the raw token)
    public long AccountId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByHash { get; set; }
}

/// <summary>Email stub outbox (Q5). Confirmation / invitation tokens are recorded, not sent.</summary>
public sealed class EmailOutboxRecord
{
    public long Id { get; set; }
    public string ToAddress { get; set; } = "";
    public string Kind { get; set; } = "";       // "confirmation" | "organizer-invite"
    public string Token { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
