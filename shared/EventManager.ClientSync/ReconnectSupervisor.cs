namespace EventManager.ClientSync;

/// <summary>
/// Drives auto-reconnect + resync with bounded backoff (US-507, BR-CS-3). <see cref="RunOnceAsync"/>
/// is the testable step; <see cref="RunLoopAsync"/> wraps it with backoff for production.
/// </summary>
public sealed class ReconnectSupervisor
{
    private readonly SyncClient _client;
    private readonly BackoffPolicy _backoff;

    public ReconnectSupervisor(SyncClient client, BackoffPolicy? backoff = null)
    {
        _client = client;
        _backoff = backoff ?? BackoffPolicy.Default;
    }

    /// <summary>One reconnect+replay attempt. Returns true on success, false on failure (no throw).</summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct = default)
    {
        try
        {
            await _client.EnsureConnectedAndReplayAsync(ct);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return false;
        }
    }

    /// <summary>Production loop: retry with bounded backoff until connected, then idle-poll.</summary>
    public async Task RunLoopAsync(TimeSpan idlePoll, CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            var ok = await RunOnceAsync(ct);
            if (ok) { attempt = 0; await Task.Delay(idlePoll, ct); }
            else { await Task.Delay(_backoff.DelayForAttempt(attempt++), ct); }
        }
    }
}
