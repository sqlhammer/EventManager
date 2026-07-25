using System.Text.Json;

namespace EventManager.Sync;

/// <summary>Strategy seam for payload serialization (P-1). System.Text.Json impl by default;
/// a MessagePack impl can be dropped in post-MVP without touching log/replay code (TSD-2).</summary>
public interface IEventSerializer
{
    ReadOnlyMemory<byte> Serialize<T>(T payload);
    T Deserialize<T>(ReadOnlyMemory<byte> payload);
}

public sealed class JsonEventSerializer : IEventSerializer
{
    private readonly JsonSerializerOptions _options;

    public JsonEventSerializer(JsonSerializerOptions? options = null) =>
        _options = options ?? new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ReadOnlyMemory<byte> Serialize<T>(T payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, _options);

    public T Deserialize<T>(ReadOnlyMemory<byte> payload) =>
        JsonSerializer.Deserialize<T>(payload.Span, _options)
            ?? throw new InvalidOperationException($"Payload deserialized to null for {typeof(T).Name}.");
}

/// <summary>Upcasts an older payload version toward the current shape (P-5, Q9).</summary>
public interface IUpcaster
{
    string EventType { get; }
    int FromVersion { get; }
    ReadOnlyMemory<byte> Upcast(ReadOnlyMemory<byte> payload);
}

/// <summary>Applies the upcaster chain on read only; stored events are never mutated (BR-1.7).</summary>
public sealed class UpcasterRegistry
{
    private readonly Dictionary<(string, int), IUpcaster> _byStep = new();
    private readonly Dictionary<string, int> _current = new();

    public UpcasterRegistry Register(IUpcaster upcaster)
    {
        _byStep[(upcaster.EventType, upcaster.FromVersion)] = upcaster;
        var to = upcaster.FromVersion + 1;
        _current[upcaster.EventType] = Math.Max(_current.GetValueOrDefault(upcaster.EventType, 0), to);
        return this;
    }

    public int CurrentVersion(string eventType) => _current.GetValueOrDefault(eventType, 0);

    public TournamentEvent Upcast(TournamentEvent evt)
    {
        var payload = evt.Payload;
        int version = evt.SchemaVersion;
        while (_byStep.TryGetValue((evt.EventType, version), out var up))
        {
            payload = up.Upcast(payload);
            version++;
        }
        return version == evt.SchemaVersion ? evt : evt with { Payload = payload, SchemaVersion = version };
    }
}
