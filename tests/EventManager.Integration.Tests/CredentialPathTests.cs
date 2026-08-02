using System.Net;
using System.Net.Http.Json;
using EventManager.Api.Auth;
using EventManager.Api.Persistence;
using EventManager.Api.Services;
using EventManager.Contracts;
using EventManager.Hub.Resilience;
using EventManager.Sync;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventManager.Integration.Tests;

/// <summary>
/// The U10 seam: a REAL hub credential, presented by the REAL adapter, to the REAL cloud ingest
/// endpoint (F4=B).
///
/// Everything else about this unit is verified against stubs on one side or the other. This is the
/// only automated test where both halves are the production code, and it exists because a credential
/// or scope regression would otherwise be caught only by a human following a markdown walkthrough.
/// Scope is deliberately narrow — the credential path, nothing else.
/// </summary>
public sealed class CredentialPathTests : IClassFixture<CloudFixture>
{
    private readonly CloudFixture _cloud;

    public CredentialPathTests(CloudFixture cloud) => _cloud = cloud;

    [Fact]
    public async Task A_valid_scoped_credential_is_accepted_by_the_real_ingest_endpoint()
    {
        var (eventId, key) = await _cloud.IssueCredentialAsync("main hub");
        var transport = _cloud.TransportFor(key);

        var ack = await transport.SendAsync(BatchFor(eventId, deviceId: 71, count: 3));

        Assert.Equal(3, ack.AcceptedCount);
        Assert.Equal(3, ack.PerDeviceHighWaterMarks[71]);
    }

    [Fact]
    public async Task A_revoked_credential_is_refused_and_the_failure_is_permanent()
    {
        var (eventId, key) = await _cloud.IssueCredentialAsync("doomed hub");
        var transport = _cloud.TransportFor(key);

        // Prove it worked before revocation, so the assertion below is about revocation and nothing else.
        await transport.SendAsync(BatchFor(eventId, deviceId: 72, count: 1));

        await _cloud.RevokeAllAsync();

        var ex = await Assert.ThrowsAsync<ReplicationFailureException>(
            () => transport.SendAsync(BatchFor(eventId, deviceId: 72, count: 1, startSeq: 2)));

        Assert.Equal(FailureKind.Permanent, ex.Failure.Kind);   // never retried — an operator must act
    }

    [Fact]
    public async Task An_expired_credential_is_refused_exactly_like_a_revoked_one()
    {
        var (eventId, key) = await _cloud.IssueCredentialAsync("expiring hub");
        var transport = _cloud.TransportFor(key);

        _cloud.Clock.Now = _cloud.Clock.Now.AddYears(1);   // past event date + grace

        var ex = await Assert.ThrowsAsync<ReplicationFailureException>(
            () => transport.SendAsync(BatchFor(eventId, deviceId: 73, count: 1)));

        Assert.Equal(FailureKind.Permanent, ex.Failure.Kind);
    }

    [Fact]
    public async Task A_credential_cannot_ingest_for_a_different_event()
    {
        var (eventId, key) = await _cloud.IssueCredentialAsync("main hub");
        var transport = _cloud.TransportFor(key);

        var ex = await Assert.ThrowsAsync<ReplicationFailureException>(
            () => transport.SendAsync(BatchFor(eventId + 12345, deviceId: 74, count: 2)));

        Assert.Equal(FailureKind.Permanent, ex.Failure.Kind);
        Assert.Contains("different event", ex.Failure.Reason);
    }

    [Fact]
    public async Task An_unknown_credential_is_refused()
    {
        var (eventId, _) = await _cloud.IssueCredentialAsync("main hub");
        var transport = _cloud.TransportFor("this-key-was-never-issued");

        var ex = await Assert.ThrowsAsync<ReplicationFailureException>(
            () => transport.SendAsync(BatchFor(eventId, deviceId: 75, count: 1)));

        Assert.Equal(FailureKind.Permanent, ex.Failure.Kind);
    }

    [Fact]
    public async Task Cursors_come_back_from_the_real_endpoint_so_a_restarted_hub_resumes()
    {
        var (eventId, key) = await _cloud.IssueCredentialAsync("main hub");
        var transport = _cloud.TransportFor(key);
        await transport.SendAsync(BatchFor(eventId, deviceId: 76, count: 4));

        var cursors = await transport.GetHighWaterMarksAsync();

        Assert.Equal(4, cursors[76]);
    }

    private static ReplicationBatchDto BatchFor(long eventScopeId, long deviceId, int count, long startSeq = 1)
    {
        var events = new List<EventEnvelopeDto>();
        for (var i = 0; i < count; i++)
        {
            var seq = startSeq + i;
            events.Add(new EventEnvelopeDto(
                EventId: deviceId * 100_000 + seq, DeviceId: deviceId, SequenceNumber: seq,
                EventType: "CheckedIn", SchemaVersion: 1, PayloadBase64: Convert.ToBase64String("{}"u8.ToArray()),
                OccurredAt: new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero).AddSeconds(seq),
                EventScopeId: eventScopeId));
        }
        return new ReplicationBatchDto(events);
    }
}
