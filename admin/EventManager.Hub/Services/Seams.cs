using EventManager.Contracts;

namespace EventManager.Hub.Services;

/// <summary>Hub self-identity: LAN address + self-signed cert fingerprint pinned by spokes (D-08, US-302/303).</summary>
public sealed class HubIdentity
{
    public string HubAddress { get; init; } = "hub.local";
    public int Port { get; init; } = 5001;
    public string CertFingerprint { get; init; } = "DEV-FINGERPRINT";
}

/// <summary>Real-time push to spokes (US-407). Concrete SignalR/WSS adapter lands with the MAUI host;
/// the default records pushes in-process so the flow is testable.</summary>
public interface IHubPush
{
    Task PushAsync(HubPushMessageDto message, CancellationToken ct = default);
}

public sealed class InProcessHubPush : IHubPush
{
    private readonly List<HubPushMessageDto> _sent = [];
    public IReadOnlyList<HubPushMessageDto> Sent => _sent;
    public Task PushAsync(HubPushMessageDto message, CancellationToken ct = default) { _sent.Add(message); return Task.CompletedTask; }
}

/// <summary>LAN discovery advertisement (mDNS). Concrete Makaretu adapter is a deferred seam; no-op default.</summary>
public interface IMdnsAdvertiser
{
    void Advertise(HubDiscoveryInfoDto info);
}

public sealed class NoopMdnsAdvertiser : IMdnsAdvertiser
{
    public HubDiscoveryInfoDto? LastAdvertised { get; private set; }
    public void Advertise(HubDiscoveryInfoDto info) => LastAdvertised = info;
}
