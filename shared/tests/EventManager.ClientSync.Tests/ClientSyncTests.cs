using EventManager.Contracts;
using EventManager.Sync;
using Xunit;

namespace EventManager.ClientSync.Tests;

public class ClientSyncTests
{
    private static DeviceCredentialRef Cred() => new(555, 7, "Judge-Mat2", "FP-ABC");

    [Fact] // BR-CS-1 durable before ack; BR-CS-7 honest queued count
    public async Task Queue_PersistsDurablyAndReportsCount()
    {
        var store = new InMemoryEventStore();
        var queue = new LocalEventQueue(store);

        await queue.EnqueueDurableAsync(Ev.Make(1, 1, 1));
        await queue.EnqueueDurableAsync(Ev.Make(2, 1, 2));

        Assert.Equal(2, await queue.QueuedCountAsync());
    }

    [Fact] // BR-CS-2 idempotent replay: second replay sends nothing new, queue drains
    public async Task Replay_IsIdempotent()
    {
        var store = new InMemoryEventStore();
        var queue = new LocalEventQueue(store);
        for (int i = 1; i <= 3; i++) await queue.EnqueueDurableAsync(Ev.Make(i, 1, i));

        var transport = new FakeTransport();
        var client = new SyncClient(transport, queue, Cred());

        await client.EnsureConnectedAndReplayAsync();
        Assert.Equal(3, transport.Received.Count);
        Assert.Equal(0, await queue.QueuedCountAsync());

        await client.EnsureConnectedAndReplayAsync(); // replay again
        Assert.Equal(3, transport.Received.Count);     // nothing new
        Assert.Equal(0, client.Status.QueuedCount);
    }

    [Fact] // BR-CS-3 reconnect: fails then succeeds without throwing
    public async Task Reconnect_RecoversFromFailure()
    {
        var store = new InMemoryEventStore();
        var queue = new LocalEventQueue(store);
        await queue.EnqueueDurableAsync(Ev.Make(1, 1, 1));

        var transport = new FakeTransport { FailConnect = true };
        var supervisor = new ReconnectSupervisor(new SyncClient(transport, queue, Cred()));

        Assert.False(await supervisor.RunOnceAsync()); // hub unreachable
        transport.FailConnect = false;
        Assert.True(await supervisor.RunOnceAsync());  // recovered
        Assert.Single(transport.Received);
    }

    [Fact] // BR-CS-4 push applied idempotently through the projection
    public void Push_IsIdempotent()
    {
        var host = new ProjectionHost<int>(new CountProjection());
        var consumer = new HubPushConsumer<int>(host);
        int changedCount = 0;
        consumer.Changed += _ => changedCount++;

        var envelope = EventEnvelopeMapper.ToDto(Ev.Make(9, 1, 1));
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(envelope);
        var msg = new HubPushMessageDto(PushType.BracketUpdated, Convert.ToBase64String(json));

        consumer.OnPush(msg);
        consumer.OnPush(msg); // duplicate push

        Assert.Equal(1, consumer.State);   // applied once
        Assert.Equal(2, changedCount);     // but both notifications raised
    }

    [Fact] // BR-CS-8 ordering + backoff bound
    public void Backoff_IsBounded()
    {
        var b = BackoffPolicy.Default;
        Assert.Equal(TimeSpan.FromSeconds(1), b.DelayForAttempt(0));
        Assert.True(b.DelayForAttempt(100) <= TimeSpan.FromSeconds(30));
    }

    [Fact] // BR-CS-5 pairing rejects an empty token
    public async Task Pairing_RejectsEmptyToken()
    {
        var client = new PairingClient(discovery: new FakeDiscovery(), transport: new FakeTransport());
        var result = await client.PairAsync("", new HubDiscoveryInfoDto("10.0.0.1", 5001, "FP"));
        Assert.True(result.IsError);
    }

    [Fact] // pairing returns a credential pinning the hub fingerprint
    public async Task Pairing_ReturnsCredentialWithPinnedFingerprint()
    {
        var client = new PairingClient(new FakeDiscovery(), new FakeTransport());
        var result = await client.PairAsync("token-123", new HubDiscoveryInfoDto("10.0.0.1", 5001, "FP"));
        Assert.False(result.IsError);
        Assert.Equal("FP-ABC", result.Value.HubCertFingerprint);
    }

    private sealed class FakeDiscovery : IHubDiscovery
    {
        public Task<IReadOnlyList<HubDiscoveryInfoDto>> DiscoverAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HubDiscoveryInfoDto>>(
                new[] { new HubDiscoveryInfoDto("10.0.0.1", 5001, "FP-ABC") });
    }
}
