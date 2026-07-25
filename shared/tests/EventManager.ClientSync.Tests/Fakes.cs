using System.Runtime.CompilerServices;
using EventManager.Contracts;
using EventManager.Sync;

namespace EventManager.ClientSync.Tests;

/// <summary>Minimal in-memory IEventStore (single-writer) for ClientSync tests.</summary>
internal sealed class InMemoryEventStore : IEventStore
{
    private readonly List<TournamentEvent> _events = new();
    private readonly HashSet<long> _ids = new();

    public Task<bool> AppendIfNotExistsAsync(TournamentEvent evt, CancellationToken ct = default)
    {
        if (!_ids.Add(evt.EventId)) return Task.FromResult(false);
        _events.Add(evt);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<TournamentEvent>> ReadStreamAsync(long deviceId, long fromSequenceExclusive, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TournamentEvent>>(
            _events.Where(e => e.DeviceId == deviceId && e.SequenceNumber > fromSequenceExclusive)
                   .OrderBy(e => e.SequenceNumber).ToList());

    public Task<long> HighWaterMarkAsync(long deviceId, CancellationToken ct = default)
    {
        long hwm = 0;
        foreach (var s in _events.Where(e => e.DeviceId == deviceId).Select(e => e.SequenceNumber).OrderBy(s => s))
            if (s == hwm + 1) hwm = s; else if (s > hwm + 1) break;
        return Task.FromResult(hwm);
    }

    public async IAsyncEnumerable<TournamentEvent> ReadAllAsync(long? fromEventIdExclusive = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in _events.Where(e => fromEventIdExclusive is null || e.EventId > fromEventIdExclusive).OrderBy(e => e.EventId))
            yield return e;
        await Task.CompletedTask;
    }

    public Task<IReadOnlyList<long>> ListDeviceIdsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<long>>(_events.Select(e => e.DeviceId).Distinct().ToList());
}

/// <summary>Fake transport that simulates a hub: dedupes on EventId, returns per-device high-water marks.</summary>
internal sealed class FakeTransport : ISyncTransport
{
    public bool FailConnect;
    public readonly HashSet<long> Received = new();
    private readonly Dictionary<long, long> _hwm = new();

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(DeviceCredentialRef credential, CancellationToken ct = default)
    {
        if (FailConnect) throw new InvalidOperationException("no hub");
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default) { IsConnected = false; return Task.CompletedTask; }

    public Task<ReplicationAckDto> SendBatchAsync(ReplicationBatchDto batch, CancellationToken ct = default)
    {
        int accepted = 0;
        foreach (var e in batch.Events.OrderBy(e => e.SequenceNumber))
        {
            if (Received.Add(e.EventId)) accepted++;
            _hwm[e.DeviceId] = Math.Max(_hwm.GetValueOrDefault(e.DeviceId, 0), e.SequenceNumber);
        }
        return Task.FromResult(new ReplicationAckDto(accepted, new Dictionary<long, long>(_hwm)));
    }

    public Task<PairingResponseDto> RedeemPairingAsync(PairingRequestDto request, HubDiscoveryInfoDto hub, CancellationToken ct = default)
        => Task.FromResult(new PairingResponseDto(555, 7, "Judge-Mat2", "FP-ABC"));

    public IDisposable SubscribePush(Action<HubPushMessageDto> onPush) => new Noop();

    private sealed class Noop : IDisposable { public void Dispose() { } }
}

internal sealed class CountProjection : IProjection<int>
{
    public int Empty => 0;
    public int Apply(int state, TournamentEvent evt) => state + 1;
}

internal static class Ev
{
    public static TournamentEvent Make(long id, long device, long seq) =>
        new(id, device, seq, "T", 1, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UnixEpoch, 42);
}
