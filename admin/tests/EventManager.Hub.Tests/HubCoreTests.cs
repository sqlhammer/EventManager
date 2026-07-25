using EventManager.Contracts;
using EventManager.Domain;
using EventManager.Hub.Events;
using EventManager.Sync;
using FsCheck.Xunit;

namespace EventManager.Hub.Tests;

/// <summary>U4a hub-core properties + examples: pairing single-use, revocation, worker-id, idempotent intake.</summary>
public sealed class HubCoreTests
{
    // ---- Pairing ----
    [Fact]
    public async Task Pairing_token_is_single_use() // US-303
    {
        using var h = new HubTestHost();
        var qr = await h.Pairing.IssueTokenAsync(eventId: 42, "Judge — Mat 2");

        var first = await h.Pairing.RedeemAsync(new PairingRequestDto(qr.EnrollmentToken, "spoke"));
        var second = await h.Pairing.RedeemAsync(new PairingRequestDto(qr.EnrollmentToken, "spoke2"));

        Assert.False(first.IsError);
        Assert.True(second.IsError);   // re-use rejected
    }

    [Fact]
    public async Task Pairing_assigns_unique_worker_ids() // Q10
    {
        using var h = new HubTestHost();
        var a = await h.Pairing.RedeemAsync(new PairingRequestDto((await h.Pairing.IssueTokenAsync(1, "Judge — Mat 1")).EnrollmentToken, "s1"));
        var b = await h.Pairing.RedeemAsync(new PairingRequestDto((await h.Pairing.IssueTokenAsync(1, "Judge — Mat 2")).EnrollmentToken, "s2"));
        Assert.NotEqual(a.Value.WorkerId, b.Value.WorkerId);
    }

    // ---- Revocation ----
    [Fact]
    public async Task Revoked_device_sync_is_rejected() // US-508
    {
        using var h = new HubTestHost();
        var paired = await h.Pairing.RedeemAsync(new PairingRequestDto((await h.Pairing.IssueTokenAsync(1, "Judge — Mat 1")).EnrollmentToken, "s1"));
        var deviceId = paired.Value.DeviceId;

        Assert.True(await h.Devices.IsActiveAsync(deviceId));
        await h.Devices.RevokeAsync(deviceId);
        Assert.False(await h.Devices.IsActiveAsync(deviceId));

        var intake = await h.Sync.IntakeAsync(deviceId, Batch(scope: 1, device: 55, count: 2));
        Assert.True(intake.IsError);   // revoked credential rejected on next contact
    }

    // ---- Idempotent intake (PBT) ----
    [Property(MaxTest = 50)] // replay never duplicates
    public void Intake_is_idempotent(byte rawCount)
    {
        using var h = new HubTestHost();
        var deviceId = PairActiveDevice(h);
        int count = 1 + (rawCount % 6);
        var batch = Batch(scope: 1, device: 55, count: count);

        var first = h.Sync.IntakeAsync(deviceId, batch).GetAwaiter().GetResult();
        var second = h.Sync.IntakeAsync(deviceId, batch).GetAwaiter().GetResult();

        Assert.False(first.IsError);
        Assert.Equal(count, first.Value.AcceptedCount);
        Assert.Equal(0, second.Value.AcceptedCount);   // replay accepts nothing
        Assert.Equal(count, h.Db.Events.Count(e => e.DeviceId == 55));
    }

    // ---- Offline RBAC deny-by-default ----
    [Fact]
    public async Task Hub_rbac_denies_unknown_organizer()
    {
        using var h = new HubTestHost();
        Assert.False(await h.Auth.IsPermittedAsync(accountId: 7, eventId: 1, OrganizerAction.ManageRoster));
    }

    private static long PairActiveDevice(HubTestHost h)
    {
        var qr = h.Pairing.IssueTokenAsync(1, "Judge — Mat 1").GetAwaiter().GetResult();
        return h.Pairing.RedeemAsync(new PairingRequestDto(qr.EnrollmentToken, "s")).GetAwaiter().GetResult().Value.DeviceId;
    }

    private static ReplicationBatchDto Batch(long scope, long device, int count)
    {
        var ser = new JsonEventSerializer();
        var list = new List<EventEnvelopeDto>();
        for (int i = 1; i <= count; i++)
        {
            var te = new TournamentEvent(2000 + i, device, i, "MatchScored", 1,
                ser.Serialize(new { n = i }), DateTimeOffset.UtcNow, scope);
            list.Add(EventEnvelopeMapper.ToDto(te));
        }
        return new ReplicationBatchDto(list);
    }
}
