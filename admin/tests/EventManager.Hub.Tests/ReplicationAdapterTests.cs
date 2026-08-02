using System.Net;
using System.Net.Http.Json;
using EventManager.Contracts;
using EventManager.Hub.Resilience;
using EventManager.Sync;
using FsCheck.Xunit;

namespace EventManager.Hub.Tests;

/// <summary>
/// U10 HTTP replication adapter (US-802..US-810). Exercises the adapter against a stubbed
/// <see cref="HttpMessageHandler"/> — classification, selective retry, circuit breaking, HTTPS
/// enforcement, cursor seeding, close-out, and credential custody — plus property P-REPL-1.
/// </summary>
public sealed class ReplicationAdapterTests
{
    // ---------------- Credential custody (US-802) ----------------

    [Fact]
    public async Task Installing_over_an_existing_credential_is_refused() // BR-REPL-22, FD-Q8=B
    {
        using var h = new HubTestHost();

        var first = await h.Credentials.InstallAsync("key-one", "https://cloud.example.org");
        var second = await h.Credentials.InstallAsync("key-two", "https://cloud.example.org");

        Assert.Equal(CredentialInstallOutcome.Installed, first);
        Assert.Equal(CredentialInstallOutcome.RefusedSlotOccupied, second);

        var stored = await h.Credentials.TryGetAsync();
        Assert.Equal("key-one", stored!.Key);   // the working credential survived the careless paste
    }

    [Fact]
    public async Task Clearing_then_installing_rotates_the_credential() // BR-REPL-22
    {
        using var h = new HubTestHost();
        await h.Credentials.InstallAsync("key-one", "https://cloud.example.org");

        await h.Credentials.ClearAsync();
        var outcome = await h.Credentials.InstallAsync("key-two", "https://cloud.example.org");

        Assert.Equal(CredentialInstallOutcome.Installed, outcome);
        Assert.Equal("key-two", (await h.Credentials.TryGetAsync())!.Key);
    }

    [Fact]
    public async Task A_non_https_cloud_address_is_refused_unless_explicitly_overridden() // BR-REPL-26
    {
        using var h = new HubTestHost();

        Assert.Equal(CredentialInstallOutcome.RefusedInsecureUrl,
            await h.Credentials.InstallAsync("key", "http://cloud.example.org"));

        h.ReplicationOptions.AllowInsecureBaseUrl = true;
        Assert.Equal(CredentialInstallOutcome.Installed,
            await h.Credentials.InstallAsync("key", "http://localhost:5000"));
    }

    [Fact]
    public async Task Existence_can_be_checked_without_revealing_the_key() // BR-REPL-24
    {
        using var h = new HubTestHost();
        Assert.False(await h.Credentials.ExistsAsync());

        await h.Credentials.InstallAsync("secret-key", "https://cloud.example.org");
        Assert.True(await h.Credentials.ExistsAsync());
    }

    // ---------------- Classification (US-804) ----------------

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, FailureKind.TransientResponse)]
    [InlineData(HttpStatusCode.BadGateway, FailureKind.TransientResponse)]
    [InlineData(HttpStatusCode.ServiceUnavailable, FailureKind.TransientResponse)]
    [InlineData(HttpStatusCode.RequestTimeout, FailureKind.TransientResponse)]
    [InlineData(HttpStatusCode.TooManyRequests, FailureKind.Throttled)]
    [InlineData(HttpStatusCode.BadRequest, FailureKind.Permanent)]
    [InlineData(HttpStatusCode.Unauthorized, FailureKind.Permanent)]
    [InlineData(HttpStatusCode.Forbidden, FailureKind.Permanent)]
    [InlineData(HttpStatusCode.NotFound, FailureKind.Permanent)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, FailureKind.Permanent)]
    public void Responses_are_classified_per_the_US_804_table(HttpStatusCode status, FailureKind expected) // BR-REPL-29..32
    {
        using var response = new HttpResponseMessage(status);
        var failure = ReplicationFailureClassifier.Classify(response);

        Assert.NotNull(failure);
        Assert.Equal(expected, failure!.Kind);
    }

    [Fact]
    public void Only_connection_failures_advance_the_breaker() // BR-REPL-34
    {
        // A 500 means the cloud is REACHABLE and unwell — a different situation from a dead link.
        using var serverError = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        Assert.False(ReplicationFailureClassifier.Classify(serverError)!.AdvancesBreaker);

        Assert.True(ReplicationFailureClassifier.Classify(new HttpRequestException("no route")).AdvancesBreaker);
        Assert.True(ReplicationFailureClassifier.Classify(new TaskCanceledException()).AdvancesBreaker);
    }

    [Fact]
    public void A_retry_after_header_is_honoured() // BR-REPL-31
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(17));

        var failure = ReplicationFailureClassifier.Classify(response);

        Assert.Equal(FailureKind.Throttled, failure!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(17), failure.RetryAfter);
    }

    [Fact]
    public void A_permanent_failure_names_the_action_an_operator_must_take() // US-804
    {
        using var unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        using var forbidden = new HttpResponseMessage(HttpStatusCode.Forbidden);

        // The two conditions need DIFFERENT responses from a human, so they must read differently.
        Assert.Contains("revoked or expired", ReplicationFailureClassifier.Classify(unauthorized)!.Reason);
        Assert.Contains("different event", ReplicationFailureClassifier.Classify(forbidden)!.Reason);
    }

    // ---------------- Circuit breaker (US-804) ----------------

    [Fact]
    public void The_breaker_opens_after_the_threshold_and_recovers_after_the_cooldown() // BR-REPL-35
    {
        using var h = new HubTestHost();
        var breaker = new ReplicationCircuitBreaker(h.ReplicationOptions, h.Clock);

        Assert.Equal(CircuitState.Closed, breaker.State);
        for (var i = 0; i < h.ReplicationOptions.BreakerFailureThreshold; i++) breaker.RecordConnectionFailure();
        Assert.Equal(CircuitState.Open, breaker.State);
        Assert.False(breaker.TryAcquire());

        h.Clock.Advance(h.ReplicationOptions.BreakerCooldown);
        Assert.Equal(CircuitState.HalfOpen, breaker.State);

        Assert.True(breaker.TryAcquire());     // exactly one trial
        Assert.False(breaker.TryAcquire());    // and no more until it reports back

        breaker.RecordSuccess();
        Assert.Equal(CircuitState.Closed, breaker.State);
        Assert.Equal(0, breaker.ConsecutiveFailures);
    }

    [Fact]
    public void A_server_error_does_not_open_the_breaker() // BR-REPL-34
    {
        using var h = new HubTestHost();
        var breaker = new ReplicationCircuitBreaker(h.ReplicationOptions, h.Clock);

        for (var i = 0; i < 10; i++) breaker.RecordNonConnectionFailure();

        Assert.Equal(CircuitState.Closed, breaker.State);
    }

    // ---------------- Transport behaviour ----------------

    [Fact]
    public async Task A_batch_is_posted_with_the_credential_header_and_the_ack_returned() // U10-FR-1
    {
        using var h = new HubTestHost();
        await h.Credentials.InstallAsync("the-key", "https://cloud.example.org");
        string? seenHeader = null;

        var transport = TransportFor(h, req =>
        {
            seenHeader = req.Headers.TryGetValues(HttpCloudReplicationTransport.CredentialHeader, out var v)
                ? string.Join("", v)
                : null;
            return Json(HttpStatusCode.OK, new ReplicationAckDto(2, new Dictionary<long, long> { [7] = 2 }));
        });

        var ack = await transport.SendAsync(new ReplicationBatchDto([]));

        Assert.Equal("the-key", seenHeader);
        Assert.Equal(2, ack.AcceptedCount);
    }

    [Fact]
    public async Task With_no_credential_installed_sending_is_a_permanent_failure_not_a_retry_loop() // BR-REPL-25
    {
        using var h = new HubTestHost();
        var transport = TransportFor(h, _ => new HttpResponseMessage(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<ReplicationFailureException>(
            () => transport.SendAsync(new ReplicationBatchDto([])));

        Assert.Equal(FailureKind.Permanent, ex.Failure.Kind);
        Assert.Contains("No cloud credential", ex.Failure.Reason);
    }

    [Fact]
    public async Task A_permanent_failure_is_not_retried() // BR-REPL-33
    {
        using var h = new HubTestHost();
        await h.Credentials.InstallAsync("the-key", "https://cloud.example.org");
        var attempts = 0;

        var transport = TransportFor(h, _ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        });
        await SeedLocalLogAsync(h, 3);
        var client = new ReplicationClient(h.Store, new ReplicationProtocol(), transport);

        await Assert.ThrowsAsync<ReplicationFailureException>(() => client.ReplicateAsync());

        Assert.Equal(1, attempts);   // not three — retrying could never help
    }

    [Fact]
    public async Task A_transient_failure_is_retried_then_succeeds() // BR-REPL-33
    {
        using var h = new HubTestHost();
        await h.Credentials.InstallAsync("the-key", "https://cloud.example.org");
        var attempts = 0;

        var transport = TransportFor(h, req =>
        {
            attempts++;
            if (attempts < 2) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return Json(HttpStatusCode.OK, AckFor(req));
        });
        await SeedLocalLogAsync(h, 3);
        var client = new ReplicationClient(h.Store, new ReplicationProtocol(), transport);

        var result = await client.ReplicateAsync();

        // One rejected attempt, then the retry succeeded and the whole backlog drained. The local log
        // holds the spoke's three events AND the hub's own pairing events, so "replicated" counts both.
        Assert.Equal(2, attempts);
        Assert.True(result.EventsReplicated >= 3);
    }

    [Fact]
    public async Task Cursor_seeding_survives_an_unreachable_cloud() // BR-REPL-41, US-805
    {
        using var h = new HubTestHost();
        await h.Credentials.InstallAsync("the-key", "https://cloud.example.org");

        var transport = TransportFor(h, _ => throw new HttpRequestException("no internet at the venue"));
        var client = new ReplicationClient(h.Store, new ReplicationProtocol(), transport);

        // Must not throw: a hub has to be able to start with no internet.
        await client.SeedCursorsAsync();
    }

    // ---------------- Signal (US-803) ----------------

    [Fact]
    public void The_append_signal_never_blocks_even_when_the_channel_is_full() // BR-REPL-37, U10-NFR-8
    {
        var signal = new ReplicationSignal();

        // Far more signals than the channel's capacity. If this blocked or threw, a cloud problem
        // could slow down the event — which inverts the offline-first premise.
        for (var i = 0; i < 10_000; i++) signal.Signal();
    }

    [Fact]
    public async Task Spoke_sync_raises_the_append_signal() // AD-Q5=C
    {
        using var h = new HubTestHost();
        var device = await PairedDeviceAsync(h);

        await h.Sync.IntakeAsync(device, BatchFor(device, 2));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await h.Signal.WaitAsync(cts.Token);   // completes only because sync signalled
    }

    // ---------------- Property (PBT-01) ----------------

    /// <summary>
    /// P-REPL-1 — for any interleaving of outages, throttling, server errors, and batch splits, the
    /// cloud log is a gap-free prefix of the hub log with no duplicates. This is the invariant the
    /// whole unit exists to preserve.
    /// </summary>
    [Property(MaxTest = 40)]
    public void Replication_is_gap_free_and_duplicate_free_under_any_failure_interleaving(
        byte rawCount, byte rawPattern)
    {
        var eventCount = 1 + (rawCount % 40);
        var pattern = BuildPattern(rawPattern);

        using var h = new HubTestHost();
        h.Credentials.InstallAsync("the-key", "https://cloud.example.org").GetAwaiter().GetResult();

        var cloud = new Dictionary<long, List<long>>();   // deviceId -> accepted sequence numbers
        var step = 0;

        var transport = TransportFor(h, req =>
        {
            var outcome = pattern[step++ % pattern.Length];
            if (outcome == 1) throw new HttpRequestException("outage");
            if (outcome == 2) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            var batch = req.Content!.ReadFromJsonAsync<ReplicationBatchDto>().GetAwaiter().GetResult()!;
            foreach (var e in batch.Events)
            {
                if (!cloud.TryGetValue(e.DeviceId, out var seqs)) cloud[e.DeviceId] = seqs = [];
                if (!seqs.Contains(e.SequenceNumber)) seqs.Add(e.SequenceNumber);   // idempotent, like the cloud
            }

            var hwm = cloud.ToDictionary(kv => kv.Key, kv => GapFree(kv.Value));
            return Json(HttpStatusCode.OK, new ReplicationAckDto(batch.Events.Count, hwm));
        });

        SeedLocalLogAsync(h, eventCount).GetAwaiter().GetResult();

        var client = new ReplicationClient(h.Store, new ReplicationProtocol(), transport, maxBatch: 5);
        for (var pass = 0; pass < 12; pass++)
        {
            try { client.ReplicateAsync().GetAwaiter().GetResult(); }
            catch (Exception) { /* an outage is a no-op; the next pass resumes */ }
        }

        foreach (var (_, seqs) in cloud)
        {
            var sorted = seqs.Order().ToList();
            Assert.Equal(sorted.Count, sorted.Distinct().Count());          // no duplicates
            for (var i = 0; i < sorted.Count; i++)
                Assert.Equal(i + 1, sorted[i]);                              // gap-free prefix
        }
    }

    /// <summary>Derives an outcome script (0 = success, 1 = outage, 2 = server error) from one byte.</summary>
    private static int[] BuildPattern(byte raw)
    {
        var pattern = new int[4];
        for (var i = 0; i < pattern.Length; i++) pattern[i] = (raw >> (i * 2)) & 0b11;
        for (var i = 0; i < pattern.Length; i++) { if (pattern[i] > 2) pattern[i] = 0; }
        return pattern;
    }

    // ---------------- Helpers ----------------

    private static long GapFree(IEnumerable<long> seqs)
    {
        long hwm = 0;
        foreach (var s in seqs.Order())
        {
            if (s == hwm + 1) hwm = s;
            else if (s > hwm + 1) break;
        }
        return hwm;
    }

    private static HttpCloudReplicationTransport TransportFor(
        HubTestHost h, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var factory = new StubHttpClientFactory(respond);
        var breaker = new ReplicationCircuitBreaker(h.ReplicationOptions, h.Clock);
        var metrics = new ReplicationMetrics(h.ReplicationStatus);
        return new HttpCloudReplicationTransport(factory, h.Credentials, breaker, h.ReplicationStatus, metrics,
            h.ReplicationOptions, Microsoft.Extensions.Logging.Abstractions.NullLogger<HttpCloudReplicationTransport>.Instance);
    }

    /// <summary>
    /// Acknowledges a batch the way the real cloud does: per-device high-water marks for every device
    /// present. A stub that acknowledged only one device would leave the others' cursors unmoved and
    /// make the client loop again — a test artefact rather than a defect.
    /// </summary>
    private static ReplicationAckDto AckFor(HttpRequestMessage req)
    {
        var batch = req.Content!.ReadFromJsonAsync<ReplicationBatchDto>().GetAwaiter().GetResult()!;
        var hwm = batch.Events
            .GroupBy(e => e.DeviceId)
            .ToDictionary(g => g.Key, g => g.Max(e => e.SequenceNumber));
        return new ReplicationAckDto(batch.Events.Count, hwm);
    }

    private static HttpResponseMessage Json<T>(HttpStatusCode status, T body) =>
        new(status) { Content = JsonContent.Create(body) };

    private static async Task<long> PairedDeviceAsync(HubTestHost h)
    {
        var qr = await h.Pairing.IssueTokenAsync(eventId: 42, "Judge — Mat 1");
        var redeemed = await h.Pairing.RedeemAsync(new PairingRequestDto(qr.EnrollmentToken, "spoke"));
        return redeemed.Value.DeviceId;
    }

    private static ReplicationBatchDto BatchFor(long deviceId, int count, long startSeq = 1)
    {
        var events = new List<EventEnvelopeDto>();
        for (var i = 0; i < count; i++)
        {
            var seq = startSeq + i;
            events.Add(new EventEnvelopeDto(
                EventId: deviceId * 1_000 + seq, DeviceId: deviceId, SequenceNumber: seq,
                EventType: "ScoreRecorded", SchemaVersion: 1, PayloadBase64: Convert.ToBase64String("{}"u8.ToArray()),
                OccurredAt: new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero).AddSeconds(seq),
                EventScopeId: 42));
        }
        return new ReplicationBatchDto(events);
    }

    /// <summary>Seeds a local log through the real spoke-sync path and returns the device id.</summary>
    private static async Task<long> SeedLocalLogAsync(HubTestHost h, int count)
    {
        var device = await PairedDeviceAsync(h);
        await h.Sync.IntakeAsync(device, BatchFor(device, count));
        return device;
    }

    /// <summary>Stub factory returning a client backed by a scripted handler.</summary>
    private sealed class StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(respond));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
