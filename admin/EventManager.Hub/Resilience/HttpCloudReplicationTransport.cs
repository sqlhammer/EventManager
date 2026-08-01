using System.Net.Http.Json;
using EventManager.Contracts;

namespace EventManager.Hub.Resilience;

/// <summary>
/// The real hub→cloud transport (U10-FR-1) — the seam U7 deferred.
///
/// POSTs a <see cref="ReplicationBatchDto"/> to the cloud ingest route, authenticated by the hub's
/// own credential rather than a person's token. Every outcome is classified (BR-REPL-29..32) and
/// surfaced as a typed failure so the client can retry transient problems and stop on permanent ones.
///
/// <see cref="IsOnline"/> reflects the circuit breaker AND whether a credential is installed: a hub
/// with nothing to authenticate with is not "failing", it simply has nothing to do (BR-REPL-25).
/// </summary>
public sealed class HttpCloudReplicationTransport(
    IHttpClientFactory httpClientFactory,
    IHubCredentialReader credentials,
    ReplicationCircuitBreaker breaker,
    ReplicationStatus status,
    ReplicationMetrics metrics,
    ReplicationOptions options,
    ILogger<HttpCloudReplicationTransport> log) : ICloudReplicationTransport
{
    public const string HttpClientName = "cloud-replication";
    public const string CredentialHeader = "X-Hub-Credential";

    private const string BatchPath = "api/ingest/batch";
    private const string CursorPath = "api/ingest/high-water-marks";

    /// <summary>
    /// Gates replication so an outage — or an uninstalled credential — is a no-op that resumes on its
    /// own, rather than an error anyone at the venue has to see.
    /// </summary>
    public bool IsOnline
    {
        get
        {
            status.RecordCircuit(breaker.State);
            return breaker.State != CircuitState.Open;
        }
    }

    public async Task<ReplicationAckDto> SendAsync(ReplicationBatchDto batch, CancellationToken ct = default)
    {
        var (client, credential, baseUri) = await PrepareAsync(ct);

        return await ExecuteAsync(async token =>
        {
            using var request = Request(HttpMethod.Post, baseUri, BatchPath, credential);
            request.Content = JsonContent.Create(batch);
            using var response = await client.SendAsync(request, token);
            var failure = ReplicationFailureClassifier.Classify(response);
            if (failure is not null) throw new ReplicationFailureException(failure);

            var ack = await response.Content.ReadFromJsonAsync<ReplicationAckDto>(token);
            if (ack is null)
                throw new ReplicationFailureException(
                    new ReplicationFailure(FailureKind.Permanent, null, "The cloud returned an unreadable acknowledgement."));

            metrics.RecordBatch(ack.AcceptedCount);
            return ack;
        }, ct);
    }

    /// <summary>
    /// Cloud cursors for this credential's event (U10-FR-12). A hub credential is already bound to one
    /// event, so it does not name it.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, long>> GetHighWaterMarksAsync(CancellationToken ct = default)
    {
        var (client, credential, baseUri) = await PrepareAsync(ct);

        return await ExecuteAsync(async token =>
        {
            using var request = Request(HttpMethod.Get, baseUri, CursorPath, credential);
            using var response = await client.SendAsync(request, token);
            var failure = ReplicationFailureClassifier.Classify(response);
            if (failure is not null) throw new ReplicationFailureException(failure);

            var cursors = await response.Content.ReadFromJsonAsync<Dictionary<long, long>>(token);
            if (cursors is null)
                throw new ReplicationFailureException(
                    new ReplicationFailure(FailureKind.Permanent, null, "The cloud returned unreadable cursors."));
            return (IReadOnlyDictionary<long, long>)cursors;
        }, ct);
    }

    /// <summary>
    /// Shared outcome handling: classify, feed the breaker only on connection failures (BR-REPL-34),
    /// and never log the credential (U10-NFR-5).
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        // BR-REPL-27 — no unbounded wait. Applied per call rather than through HttpClient.Timeout so
        // the transport never mutates a client it does not own.
        using var deadline = new CancellationTokenSource(options.RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);

        try
        {
            var result = await action(linked.Token);
            breaker.RecordSuccess();
            return result;
        }
        catch (ReplicationFailureException ex)
        {
            Observe(ex.Failure);
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // shutdown, not a replication failure
        }
        catch (Exception ex)
        {
            var failure = ReplicationFailureClassifier.Classify(ex);
            Observe(failure);
            throw new ReplicationFailureException(failure);
        }
    }

    private void Observe(ReplicationFailure failure)
    {
        metrics.RecordFailure(failure.Kind);

        if (failure.AdvancesBreaker) breaker.RecordConnectionFailure();
        else breaker.RecordNonConnectionFailure();

        status.RecordFailure(failure, breaker.ConsecutiveFailures);
        status.RecordCircuit(breaker.State);

        if (failure.Kind == FailureKind.Permanent)
            log.LogWarning("Replication stopped: {Reason}", failure.Reason);
        else
            log.LogDebug("Replication attempt failed ({Kind}): {Reason}", failure.Kind, failure.Reason);
    }

    /// <summary>
    /// Builds a client carrying the credential. Refuses a non-HTTPS base URL unless the development
    /// override is explicitly enabled (BR-REPL-26) — checked here as well as at install, because the
    /// base URL also arrives from configuration.
    /// </summary>
    private async Task<(HttpClient Client, HubCloudCredential Credential, Uri BaseUri)> PrepareAsync(CancellationToken ct)
    {
        var credential = await credentials.TryGetAsync(ct);
        if (credential is null)
            throw new ReplicationFailureException(new ReplicationFailure(
                FailureKind.Permanent, null, "No cloud credential is installed on this hub."));

        if (!Uri.TryCreate(credential.CloudBaseUrl, UriKind.Absolute, out var baseUri))
            throw new ReplicationFailureException(new ReplicationFailure(
                FailureKind.Permanent, null, "The configured cloud address is not a valid URL."));

        var isHttps = string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !options.AllowInsecureBaseUrl)
            throw new ReplicationFailureException(new ReplicationFailure(
                FailureKind.Permanent, null, "The cloud address must use HTTPS."));

        // The client is NOT mutated: base address, credential, and timeout are applied per request.
        // Mutating a client obtained from the factory would break the moment one is reused, and it is
        // not ours to configure.
        return (httpClientFactory.CreateClient(HttpClientName), credential, new Uri(baseUri, "/"));
    }

    /// <summary>Builds a self-contained request — absolute URI and credential header, no client state.</summary>
    private static HttpRequestMessage Request(HttpMethod method, Uri baseUri, string path, HubCloudCredential credential)
    {
        var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Headers.Add(CredentialHeader, credential.Key);
        return request;
    }
}
