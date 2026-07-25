using ErrorOr;
using EventManager.Contracts;

namespace EventManager.ClientSync;

/// <summary>
/// Discovers the hub and redeems a one-time enrollment token (FR-4.3/4.4). The cert fingerprint
/// is pinned in the returned credential and enforced by the transport on every later connection
/// (BR-CS-5/6).
/// </summary>
public sealed class PairingClient
{
    private readonly IHubDiscovery _discovery;
    private readonly ISyncTransport _transport;

    public PairingClient(IHubDiscovery discovery, ISyncTransport transport)
    {
        _discovery = discovery;
        _transport = transport;
    }

    public Task<IReadOnlyList<HubDiscoveryInfoDto>> DiscoverAsync(CancellationToken ct = default)
        => _discovery.DiscoverAsync(ct);

    public async Task<ErrorOr<DeviceCredentialRef>> PairAsync(string enrollmentToken, HubDiscoveryInfoDto hub, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(enrollmentToken))
            return Error.Validation("Pairing.EmptyToken", "Enrollment token is required.");

        var response = await _transport.RedeemPairingAsync(
            new PairingRequestDto(enrollmentToken, hub.HubAddress), hub, ct);

        // Pin the hub's cert fingerprint from the pairing response.
        return new DeviceCredentialRef(response.DeviceId, response.WorkerId, response.RoleDescriptor, response.HubCertFingerprint);
    }
}
