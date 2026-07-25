using EventManager.Contracts;
using EventManager.Hub.Events;
using EventManager.Hub.Persistence;
using EventManager.Sync;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Hub.Services;

/// <summary>QR payload a spoke scans to pair: hub address + pinned cert fingerprint + one-time token + role.</summary>
public sealed record PairingQr(string HubAddress, int Port, string CertFingerprint, string EnrollmentToken, string RoleDescriptor);

/// <summary>
/// Device pairing (US-303/304). Issues one-time tokens and redeems them exactly once, assigning a
/// Snowflake worker id (U1 WorkerIdRegistry) + device credential and emitting a DevicePaired event.
/// Manual-IP fallback (US-304) uses the identical token path.
/// </summary>
public sealed class PairingService(
    HubDbContext db, HubEventWriter writer, IIdGenerator ids, IWorkerIdRegistry workers, HubIdentity hub)
{
    public async Task<PairingQr> IssueTokenAsync(long eventId, string roleDescriptor, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N");
        db.PairingTokens.Add(new PairingTokenRecord
        {
            Token = token, EventId = eventId, RoleDescriptor = roleDescriptor,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15), Consumed = false,
        });
        await db.SaveChangesAsync(ct);
        return new PairingQr(hub.HubAddress, hub.Port, hub.CertFingerprint, token, roleDescriptor);
    }

    public async Task<ErrorOr<PairingResponseDto>> RedeemAsync(PairingRequestDto request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var token = await db.PairingTokens.FirstOrDefaultAsync(t => t.Token == request.EnrollmentToken, ct);
        if (token is null) return Error.NotFound("Pairing.Token", "Unknown enrollment token.");
        if (token.Consumed) return Error.Conflict("Pairing.Used", "Enrollment token already used."); // single-use (US-303)
        if (token.ExpiresAt <= DateTimeOffset.UtcNow) return Error.Conflict("Pairing.Expired", "Enrollment token expired.");

        token.Consumed = true;                                   // consume before granting
        var deviceId = ids.NextId();
        var workerId = workers.Assign(deviceId);                 // Snowflake worker id (Q10)

        await writer.AppendAsync(token.EventId, HubEventTypes.DevicePaired,
            new DevicePairedPayload(deviceId, token.EventId, token.RoleDescriptor, workerId), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new PairingResponseDto(deviceId, workerId, token.RoleDescriptor, hub.CertFingerprint);
    }
}
