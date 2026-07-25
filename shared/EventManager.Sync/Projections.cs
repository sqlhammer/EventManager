namespace EventManager.Sync;

/// <summary>A projection reduces the event log into a read-model state (P-4). Pure Apply.</summary>
public interface IProjection<TState>
{
    TState Empty { get; }
    TState Apply(TState state, TournamentEvent evt);
}

/// <summary>
/// Hosts a single in-memory projection (Q3=A). Rebuild folds the log in ascending EventId order
/// (canonical, Q7) so state is deterministic regardless of arrival order; Dispatch updates
/// incrementally. Both dedupe on EventId, so replay is idempotent (BR-1.3/1.4).
/// </summary>
public sealed class ProjectionHost<TState>
{
    private readonly IProjection<TState> _projection;
    private readonly HashSet<long> _seen = new();

    public TState State { get; private set; }

    public ProjectionHost(IProjection<TState> projection)
    {
        _projection = projection;
        State = projection.Empty;
    }

    public TState Rebuild(IEnumerable<TournamentEvent> events)
    {
        State = _projection.Empty;
        _seen.Clear();
        foreach (var e in events.OrderBy(e => e.EventId))
            ApplyInternal(e);
        return State;
    }

    public void Dispatch(TournamentEvent evt) => ApplyInternal(evt);

    private void ApplyInternal(TournamentEvent e)
    {
        if (!_seen.Add(e.EventId)) return; // idempotent
        State = _projection.Apply(State, e);
    }
}
