using System.ComponentModel.DataAnnotations;

namespace EventManager.Hub.Resilience;

/// <summary>
/// Every replication knob, with defaults from NFR Design §5 (ND-Q8=A). Validated on start so a bad
/// value fails at startup with a clear message rather than at 2am mid-event.
/// </summary>
public sealed class ReplicationOptions : IValidatableObject
{
    public const string SectionName = "Replication";

    /// <summary>Cloud base URL. Must be HTTPS unless <see cref="AllowInsecureBaseUrl"/> (BR-REPL-26).</summary>
    public string? CloudBaseUrl { get; set; }

    /// <summary>Development escape hatch only. MUST never be true outside development.</summary>
    public bool AllowInsecureBaseUrl { get; set; }

    [Range(1, 300)] public int RequestTimeoutSeconds { get; set; } = 30;            // BR-REPL-27
    [Range(1, 20)] public int BreakerFailureThreshold { get; set; } = 3;            // BR-REPL-35
    [Range(5, 1800)] public int BreakerCooldownSeconds { get; set; } = 60;          // BR-REPL-35
    [Range(0, 60)] public int AppendDebounceSeconds { get; set; } = 2;              // BR-REPL-38
    [Range(5, 1800)] public int DrainIntervalSeconds { get; set; } = 60;            // BR-REPL-39
    [Range(10, 1800)] public int CloseOutWindowSeconds { get; set; } = 120;         // BR-REPL-40
    [Range(1, 5000)] public int MaxEnvelopesPerBatch { get; set; } = 500;           // BR-REPL-28
    [Range(65536, 16 * 1024 * 1024)] public int MaxBatchBytes { get; set; } = 4 * 1024 * 1024;
    [Range(1, 10)] public int MaxRetryAttempts { get; set; } = 3;                   // BR-REPL-33
    [Range(1, 90)] public int ExpiryWarningDays { get; set; } = 7;                  // BR-REPL-16

    /// <summary>
    /// The server's ingest body cap. Kept here so the cross-field rule below can be checked at
    /// startup instead of being discovered as a 413 during an event.
    /// </summary>
    [Range(65536, 64 * 1024 * 1024)] public int ServerBodyLimitBytes { get; set; } = 8 * 1024 * 1024;

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds);
    public TimeSpan BreakerCooldown => TimeSpan.FromSeconds(BreakerCooldownSeconds);
    public TimeSpan AppendDebounce => TimeSpan.FromSeconds(AppendDebounceSeconds);
    public TimeSpan DrainInterval => TimeSpan.FromSeconds(DrainIntervalSeconds);
    public TimeSpan CloseOutWindow => TimeSpan.FromSeconds(CloseOutWindowSeconds);

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        // P-12: the client cap must be strictly below the server's, so a conforming hub can never
        // produce a 413 — that guarantee is only real if it is enforced.
        if (MaxBatchBytes >= ServerBodyLimitBytes)
            yield return new ValidationResult(
                $"{nameof(MaxBatchBytes)} ({MaxBatchBytes}) must be strictly less than {nameof(ServerBodyLimitBytes)} ({ServerBodyLimitBytes}).",
                [nameof(MaxBatchBytes)]);

        if (CloudBaseUrl is not null && !Uri.TryCreate(CloudBaseUrl, UriKind.Absolute, out _))
            yield return new ValidationResult($"{nameof(CloudBaseUrl)} must be an absolute URI.", [nameof(CloudBaseUrl)]);
    }
}
