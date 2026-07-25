using EventManager.Contracts;

namespace EventManager.ClientSync;

/// <summary>
/// Transport seam (U2-TSD-4). ClientSync orchestrates against this interface; the concrete
/// SignalR/WSS adapter is provided at app wiring, and fakes are used in unit tests.
/// </summary>
public interface ISyncTransport
{
    bool IsConnected { get; }
    Task ConnectAsync(DeviceCredentialRef credential, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task<ReplicationAckDto> SendBatchAsync(ReplicationBatchDto batch, CancellationToken ct = default);
    Task<PairingResponseDto> RedeemPairingAsync(PairingRequestDto request, HubDiscoveryInfoDto hub, CancellationToken ct = default);
    IDisposable SubscribePush(Action<HubPushMessageDto> onPush);
}

/// <summary>Hub discovery: mDNS with manual-IP / QR fallback (FR-4.3).</summary>
public interface IHubDiscovery
{
    Task<IReadOnlyList<HubDiscoveryInfoDto>> DiscoverAsync(CancellationToken ct = default);
}
