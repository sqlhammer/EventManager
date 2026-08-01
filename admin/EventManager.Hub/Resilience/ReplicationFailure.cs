using System.Net;

namespace EventManager.Hub.Resilience;

/// <summary>
/// What kind of failure occurred (BR-REPL-29..32). The distinction that matters most is between
/// <see cref="TransientConnection"/> and <see cref="TransientResponse"/>: only the former advances
/// the circuit breaker, because a server-side failure means the cloud is REACHABLE and unwell, which
/// is a different situation from a dead venue link.
/// </summary>
public enum FailureKind
{
    TransientConnection,
    TransientResponse,
    Throttled,
    Permanent,
}

public sealed record ReplicationFailure(FailureKind Kind, TimeSpan? RetryAfter, string Reason)
{
    public bool IsRetryable => Kind != FailureKind.Permanent;
    public bool AdvancesBreaker => Kind == FailureKind.TransientConnection;
}

/// <summary>Raised by the transport so the client can act on a classified outcome (BR-REPL-33).</summary>
public sealed class ReplicationFailureException(ReplicationFailure failure)
    : Exception($"Replication failed ({failure.Kind}): {failure.Reason}")
{
    public ReplicationFailure Failure { get; } = failure;
}

/// <summary>
/// Maps a transport outcome to a failure kind. A pure function with no dependencies, deliberately —
/// its behaviour is security-relevant and it must be trivially testable (P-REPL-1 relies on it).
/// </summary>
public static class ReplicationFailureClassifier
{
    public static ReplicationFailure Classify(Exception exception)
    {
        if (exception is TaskCanceledException or TimeoutException)
            return new ReplicationFailure(FailureKind.TransientConnection, null, "Request timed out.");

        if (exception is HttpRequestException)
            return new ReplicationFailure(FailureKind.TransientConnection, null, "Could not reach the cloud.");

        // Anything else is unexpected. Fail closed: treat it as permanent rather than retrying
        // something we do not understand (SECURITY-15).
        return new ReplicationFailure(FailureKind.Permanent, null, exception.GetType().Name);
    }

    public static ReplicationFailure? Classify(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return null;

        var status = response.StatusCode;

        if (status == HttpStatusCode.TooManyRequests)
            return new ReplicationFailure(FailureKind.Throttled, RetryAfterOf(response), "Cloud is throttling.");

        if (status == HttpStatusCode.RequestTimeout)
            return new ReplicationFailure(FailureKind.TransientResponse, RetryAfterOf(response), "Server request timeout.");

        if ((int)status >= 500)
            return new ReplicationFailure(FailureKind.TransientResponse, RetryAfterOf(response), $"Cloud error {(int)status}.");

        // 400, 401, 403, 404, 413, 422 and every other 4xx: retrying cannot help.
        return new ReplicationFailure(FailureKind.Permanent, null, PermanentReason(status));
    }

    /// <summary>BR-REPL-31 — honour the wait the cloud asks for when it supplies one.</summary>
    private static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return null;
        if (header.Delta is not null) return header.Delta;
        if (header.Date is not null)
        {
            var delay = header.Date.Value - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) return delay;
        }
        return null;
    }

    /// <summary>
    /// Reasons are distinct so an operator can act: an outage needs waiting, a revoked credential
    /// needs a new credential (US-804).
    /// </summary>
    private static string PermanentReason(HttpStatusCode status)
    {
        if (status == HttpStatusCode.Unauthorized)
            return "The hub credential was not accepted. It may have been revoked or expired — issue a new one.";
        if (status == HttpStatusCode.Forbidden)
            return "The hub credential is not authorized for this event. It may belong to a different event.";
        if (status == HttpStatusCode.RequestEntityTooLarge)
            return "The cloud rejected the batch as too large.";
        return $"The cloud rejected the request ({(int)status}).";
    }
}
