using EventManager.Contracts;

namespace EventManager.ClientSync;

/// <summary>
/// Connects to the hub and replays the pending queue idempotently (BR-CS-2). Exposes a
/// thread-safe <see cref="SyncStatus"/> snapshot.
/// </summary>
public sealed class SyncClient
{
    private readonly ISyncTransport _transport;
    private readonly LocalEventQueue _queue;
    private readonly DeviceCredentialRef _credential;
    private readonly object _gate = new();
    private SyncStatus _status;

    public SyncClient(ISyncTransport transport, LocalEventQueue queue, DeviceCredentialRef credential)
    {
        _transport = transport;
        _queue = queue;
        _credential = credential;
        _status = new SyncStatus(ConnectionState.Disconnected, 0, 0, null);
    }

    public SyncStatus Status { get { lock (_gate) return _status; } }

    /// <summary>Ensure connected, then replay pending events. Idempotent: re-running sends nothing new.</summary>
    public async Task EnsureConnectedAndReplayAsync(CancellationToken ct = default)
    {
        if (!_transport.IsConnected)
        {
            SetConnection(ConnectionState.Connecting);
            await _transport.ConnectAsync(_credential, ct);
        }
        SetConnection(ConnectionState.Connected);

        var pending = await _queue.PendingAsync(ct);
        if (pending.Count > 0)
        {
            var batch = new ReplicationBatchDto(pending.Select(EventEnvelopeMapper.ToDto).ToList());
            var ack = await _transport.SendBatchAsync(batch, ct);
            _queue.MarkAcked(ack.PerDeviceHighWaterMarks);
        }

        await RefreshStatusAsync(ConnectionState.Connected, ct);
    }

    private async Task RefreshStatusAsync(ConnectionState connection, CancellationToken ct)
    {
        int queued = await _queue.QueuedCountAsync(ct);
        lock (_gate)
            _status = new SyncStatus(connection, queued, _queue.LastAckedSequence(), DateTimeOffset.UtcNow);
    }

    private void SetConnection(ConnectionState connection)
    {
        lock (_gate) _status = _status with { Connection = connection };
    }
}
