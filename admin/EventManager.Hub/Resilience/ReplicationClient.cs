using EventManager.Contracts;
using EventManager.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace EventManager.Hub.Resilience;

public sealed record ReplicationResult(bool Attempted, int EventsReplicated);
public sealed record CompletenessReport(bool IsComplete, long LocalEventCount, long ReplicatedEventCount);

/// <summary>
/// Drives hub→cloud replication (US-504) and verifies post-event completeness (US-602). Uses the U1
/// <see cref="IReplicationProtocol"/> to compute the next batch above each device's cloud high-water
/// mark, sends via <see cref="ICloudReplicationTransport"/> with bounded retry/backoff, and advances the
/// cursors from the ack — so an outage is a no-op that resumes gap-free, and re-runs never duplicate.
///
/// U10 (AD-Q4=B) also makes this the SCHEDULER: it runs as a <see cref="BackgroundService"/> reacting
/// to an append signal, a drain timer, and an explicit close-out. Retry became selective — only
/// transient failures are retried (BR-REPL-33) — where it previously retried every exception,
/// including ones that could never succeed.
///
/// Being long-lived while <c>IEventStore</c> is scoped is the sharp edge here: it resolves a store
/// inside a per-run scope (CL-1=A) and never holds one. A captive scoped DbContext would not fail at
/// startup — it would corrupt intermittently under concurrency, on the component whose whole purpose
/// is guaranteeing no data is lost.
/// </summary>
public sealed class ReplicationClient : BackgroundService
{
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IEventStore? _directStore;
    private readonly IReplicationProtocol _protocol;
    private readonly ICloudReplicationTransport _transport;
    private readonly ReplicationSignal? _signal;
    private readonly ReplicationStatus? _status;
    private readonly ReplicationOptions _options;
    private readonly ILogger<ReplicationClient>? _log;

    private readonly Dictionary<long, long> _cloudHighWaterMarks = new();
    private bool _cursorsSeeded;

    /// <summary>
    /// Direct-drive constructor: the caller supplies the store. Used by tests and by any caller that
    /// wants to run a single replication pass without hosting the service (U7's original shape).
    /// </summary>
    public ReplicationClient(
        IEventStore local, IReplicationProtocol protocol, ICloudReplicationTransport transport,
        int maxBatch = 500, int maxAttempts = 3)
    {
        _directStore = local;
        _protocol = protocol;
        _transport = transport;
        _options = new ReplicationOptions { MaxEnvelopesPerBatch = maxBatch, MaxRetryAttempts = maxAttempts };
    }

    /// <summary>Hosted constructor: a scope — and therefore a fresh store — is created per run.</summary>
    public ReplicationClient(
        IServiceScopeFactory scopeFactory, IReplicationProtocol protocol, ICloudReplicationTransport transport,
        ReplicationSignal signal, ReplicationStatus status, ReplicationOptions options,
        ILogger<ReplicationClient> log)
    {
        _scopeFactory = scopeFactory;
        _protocol = protocol;
        _transport = transport;
        _signal = signal;
        _status = status;
        _options = options;
        _log = log;
    }

    // ---------------------------------------------------------------------
    // Scheduling (U10, D-U10-05 / F2=C)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Append-driven with a debounce, plus a drain timer. The timer is not redundant: it is what
    /// reopens the circuit breaker's cool-down and what drains a backlog after the log goes quiet —
    /// an append-only trigger would strand it (F2=C).
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SeedCursorsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await WaitForWorkAsync(stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await ReplicateAsync(stoppingToken);
            }
            catch (ReplicationFailureException)
            {
                // Already classified, observed, and reflected in status. Nothing at the venue should
                // see it, and the next tick will try again if it is worth trying.
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task WaitForWorkAsync(CancellationToken ct)
    {
        if (_signal is null)
        {
            await Task.Delay(_options.DrainInterval, ct);
            return;
        }

        using var timer = new CancellationTokenSource(_options.DrainInterval);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);
        try
        {
            await _signal.WaitAsync(linked.Token);
            // Debounce: let a burst of appends settle into one batch rather than one round trip each.
            if (_options.AppendDebounce > TimeSpan.Zero) await Task.Delay(_options.AppendDebounce, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Drain-timer tick — the backstop, not an error.
        }
    }

    /// <summary>
    /// Resume from where the cloud actually is (US-805). Deliberately non-blocking on failure: a hub
    /// must be able to start at a venue with no internet, and re-sending is wasteful but never
    /// incorrect (BR-REPL-41).
    /// </summary>
    public async Task SeedCursorsAsync(CancellationToken ct = default)
    {
        if (_cursorsSeeded) return;
        if (_transport is not HttpCloudReplicationTransport http) { _cursorsSeeded = true; return; }

        try
        {
            var cursors = await http.GetHighWaterMarksAsync(ct);
            foreach (var (deviceId, hwm) in cursors) _cloudHighWaterMarks[deviceId] = hwm;
            _log?.LogInformation("Seeded replication cursors for {DeviceCount} devices from the cloud.", cursors.Count);
        }
        catch (Exception)
        {
            _log?.LogInformation("Could not reach the cloud at startup; starting with empty cursors. "
                                 + "Already-replicated events will be re-sent and skipped — wasteful, not incorrect.");
        }
        _cursorsSeeded = true;
    }

    /// <summary>
    /// Close-out (US-807): drive replication to completion within a bounded window, then report
    /// completeness. Bounded rather than open-ended — close-out happens while someone is packing up,
    /// and a call that never returns at a venue with no internet is worse than an honest incomplete
    /// answer (FD-Q6=A).
    /// </summary>
    public async Task<CompletenessReport> FlushForCloseOutAsync(CancellationToken ct = default)
    {
        using var window = new CancellationTokenSource(_options.CloseOutWindow);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, window.Token);

        try
        {
            await ReplicateAsync(linked.Token);
        }
        catch (Exception)
        {
            // Report whatever completeness was actually reached rather than failing the call.
        }

        return await VerifyCompletenessAsync(ct);
    }

    // ---------------------------------------------------------------------
    // Driving (U7, unchanged in substance)
    // ---------------------------------------------------------------------

    public async Task<ReplicationResult> ReplicateAsync(CancellationToken ct = default)
    {
        if (!_transport.IsOnline) return new ReplicationResult(Attempted: false, EventsReplicated: 0);

        var replicated = 0;
        await WithStoreAsync(async local =>
        {
            while (true)
            {
                var batch = await _protocol.NextBatchAsync(local, _cloudHighWaterMarks, _options.MaxEnvelopesPerBatch, ct);
                if (batch.Count == 0) break;

                var progressedBefore = SnapshotCursorSum();
                var dto = new ReplicationBatchDto(batch.Select(EventEnvelopeMapper.ToDto).ToList());
                var ack = await SendWithRetryAsync(dto, ct);

                foreach (var (deviceId, hwm) in ack.PerDeviceHighWaterMarks)
                {
                    var current = _cloudHighWaterMarks.GetValueOrDefault(deviceId, 0);
                    _cloudHighWaterMarks[deviceId] = Math.Max(current, hwm);
                }
                replicated += ack.AcceptedCount;

                // Safety: if the cursor did not advance, stop to avoid an infinite loop.
                if (SnapshotCursorSum() == progressedBefore) break;
            }
            await UpdateStatusAsync(local, ct);
        }, ct);

        return new ReplicationResult(Attempted: true, EventsReplicated: replicated);
    }

    /// <summary>
    /// The human-facing status view (ND-Q6=C). Unlike <c>/health</c> and the metrics gauges, which
    /// serve values cached from the last run, this recomputes backlog and lag TOGETHER in one store
    /// pass — returning a live lag beside a stale count in the same response would be incoherent.
    /// It is read rarely, and usually because something looks wrong, which is exactly when a stale
    /// answer is least useful.
    /// </summary>
    public async Task<ReplicationStatusSnapshot> ComputeStatusAsync(CancellationToken ct = default)
    {
        var cached = _status?.Snapshot();
        long pending = 0;
        var lagSeconds = 0d;

        await WithStoreAsync(async local =>
        {
            var measured = await MeasureBacklogAsync(local, _cloudHighWaterMarks, ct);
            pending = measured.Pending;
            lagSeconds = measured.LagSeconds;
        }, ct);

        return new ReplicationStatusSnapshot(
            CredentialInstalled: cached?.CredentialInstalled ?? false,
            LastSuccessAt: cached?.LastSuccessAt,
            PendingEvents: pending,
            LagSeconds: lagSeconds,
            ConsecutiveFailures: cached?.ConsecutiveFailures ?? 0,
            CircuitState: cached?.CircuitState ?? nameof(Resilience.CircuitState.Closed),
            LastPermanentFailure: cached?.LastPermanentFailure,
            AsOfLastRun: false);
    }

    /// <summary>Post-event completeness (US-602): every local event is mirrored to the cloud.</summary>
    public async Task<CompletenessReport> VerifyCompletenessAsync(CancellationToken ct = default)
    {
        long localCount = 0;
        long replicatedCount = 0;
        await WithStoreAsync(async local =>
        {
            foreach (var deviceId in await local.ListDeviceIdsAsync(ct))
            {
                var localHwm = await local.HighWaterMarkAsync(deviceId, ct);
                localCount += localHwm;
                replicatedCount += Math.Min(localHwm, _cloudHighWaterMarks.GetValueOrDefault(deviceId, 0));
            }
        }, ct);
        return new CompletenessReport(localCount == replicatedCount, localCount, replicatedCount);
    }

    /// <summary>
    /// Retries only what can succeed (BR-REPL-33). A permanent failure — a revoked credential, a
    /// batch for the wrong event — propagates immediately and consumes no attempt, because retrying
    /// it three times would only delay telling the operator something they must act on.
    /// </summary>
    private async Task<ReplicationAckDto> SendWithRetryAsync(ReplicationBatchDto dto, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _transport.SendAsync(dto, ct);
            }
            catch (ReplicationFailureException ex) when (ex.Failure.IsRetryable && attempt < _options.MaxRetryAttempts)
            {
                await Task.Delay(DelayFor(ex.Failure, attempt), ct);
            }
            catch (Exception) when (attempt < _options.MaxRetryAttempts && !IsClassified())
            {
                // The in-process transport (and any other implementation) does not raise classified
                // failures; preserve U7's original blanket retry for those.
                await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1)), ct);
            }
        }
    }

    /// <summary>Honour the wait the cloud asked for; otherwise the existing exponential ladder.</summary>
    private static TimeSpan DelayFor(ReplicationFailure failure, int attempt)
    {
        if (failure.RetryAfter is not null) return failure.RetryAfter.Value;
        return TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1));
    }

    private bool IsClassified() => _transport is HttpCloudReplicationTransport;

    private async Task UpdateStatusAsync(IEventStore local, CancellationToken ct)
    {
        if (_status is null) return;
        var (pending, lagSeconds) = await MeasureBacklogAsync(local, _cloudHighWaterMarks, ct);
        _status.RecordSuccess(DateTimeOffset.UtcNow, pending, lagSeconds);
    }

    /// <summary>
    /// Backlog depth and replication lag in one pass over the store (BR-REPL-45, ND-Q6=C).
    ///
    /// Lag is the age of the OLDEST unreplicated event, not the time since the last successful run —
    /// the latter climbs indefinitely while a hub sits idle with nothing to send, which is precisely
    /// when the cloud is most current. With no backlog, lag is zero.
    /// </summary>
    internal static async Task<(long Pending, double LagSeconds)> MeasureBacklogAsync(
        IEventStore local, IReadOnlyDictionary<long, long> cursors, CancellationToken ct)
    {
        long pending = 0;
        DateTimeOffset? oldestUnreplicated = null;

        foreach (var deviceId in await local.ListDeviceIdsAsync(ct))
        {
            var cursor = cursors.GetValueOrDefault(deviceId, 0);
            var localHwm = await local.HighWaterMarkAsync(deviceId, ct);
            if (localHwm <= cursor) continue;

            pending += localHwm - cursor;

            var stream = await local.ReadStreamAsync(deviceId, cursor, ct);
            if (stream.Count == 0) continue;

            var first = stream[0].OccurredAt;
            if (oldestUnreplicated is null || first < oldestUnreplicated.Value) oldestUnreplicated = first;
        }

        if (oldestUnreplicated is null) return (pending, 0d);

        var lag = DateTimeOffset.UtcNow - oldestUnreplicated.Value;
        if (lag < TimeSpan.Zero) return (pending, 0d);
        return (pending, lag.TotalSeconds);
    }

    /// <summary>
    /// Runs the body against a store. When hosted, that means a fresh scope per run (CL-1=A); when
    /// direct-driven, the store supplied at construction.
    /// </summary>
    private async Task WithStoreAsync(Func<IEventStore, Task> body, CancellationToken ct)
    {
        if (_directStore is not null) { await body(_directStore); return; }

        using var scope = _scopeFactory!.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IEventStore>();
        await body(store);
    }

    private long SnapshotCursorSum()
    {
        long sum = 0;
        foreach (var v in _cloudHighWaterMarks.Values) sum += v;
        return sum;
    }
}
