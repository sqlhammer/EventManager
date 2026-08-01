using System.Diagnostics.Metrics;

namespace EventManager.Hub.Resilience;

/// <summary>
/// Hub replication instruments (U10-FR-18, ND-Q7=A). Uses <see cref="Meter"/> from the BCL rather
/// than an OpenTelemetry type, so component code carries no exporter dependency and the exporter is
/// swappable at the composition root (AD-Q8=A).
///
/// No instrument, tag, or label carries credential material (U10-NFR-5) — failures are tagged by
/// KIND, never by which credential produced them.
/// </summary>
public sealed class ReplicationMetrics : IDisposable
{
    public const string MeterName = "EventManager.Hub.Replication";

    private readonly Meter _meter;
    private readonly Counter<long> _eventsSent;
    private readonly Counter<long> _batches;
    private readonly Counter<long> _failures;

    public ReplicationMetrics(ReplicationStatus status)
    {
        _meter = new Meter(MeterName);
        _eventsSent = _meter.CreateCounter<long>("eventmanager.replication.events.sent", "{event}");
        _batches = _meter.CreateCounter<long>("eventmanager.replication.batches", "{batch}");
        _failures = _meter.CreateCounter<long>("eventmanager.replication.failures", "{failure}");

        // Gauges read the cached snapshot — never the store — so scraping cannot add load to a hub
        // that is running an event (P-13).
        _meter.CreateObservableGauge("eventmanager.replication.backlog", () => status.Snapshot().PendingEvents, "{event}");
        _meter.CreateObservableGauge("eventmanager.replication.lag.seconds", () => status.Snapshot().LagSeconds ?? 0d, "s");
        _meter.CreateObservableGauge("eventmanager.replication.circuit.open", () => CircuitGauge(status), "{state}");
    }

    public void RecordBatch(int eventsAccepted)
    {
        _batches.Add(1);
        _eventsSent.Add(eventsAccepted);
    }

    public void RecordFailure(FailureKind kind) => _failures.Add(1, KeyValuePair.Create<string, object?>("kind", kind.ToString()));

    private static int CircuitGauge(ReplicationStatus status)
    {
        if (status.Circuit == CircuitState.Closed) return 0;
        return 1;
    }

    public void Dispose() => _meter.Dispose();
}
