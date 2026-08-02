using System.Threading.Channels;

namespace EventManager.Hub.Resilience;

/// <summary>
/// Tells the replication loop that something was appended (AD-Q5=C, BR-REPL-37).
///
/// Non-blocking and drop-on-full, deliberately. The signal carries no data — it only says "something
/// happened" — so a dropped one costs at most one drain-timer interval. Blocking an append until the
/// replication channel had room would let a cloud problem slow down the event, which inverts the
/// entire offline-first premise (U10-NFR-8).
/// </summary>
public sealed class ReplicationSignal
{
    private readonly Channel<byte> _channel = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(capacity: 1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

    /// <summary>Never blocks, never throws, never fails an append.</summary>
    public void Signal() => _channel.Writer.TryWrite(0);

    public async Task WaitAsync(CancellationToken ct)
    {
        await _channel.Reader.WaitToReadAsync(ct);
        _channel.Reader.TryRead(out _);
    }
}
