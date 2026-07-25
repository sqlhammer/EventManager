namespace EventManager.Sync;

/// <summary>Idempotent fold primitive (P-3/P-4). Re-applying an already-seen EventId is a no-op.</summary>
public interface IReplayEngine
{
    TState Fold<TState>(TState seed, IEnumerable<TournamentEvent> events, Func<TState, TournamentEvent, TState> apply);
}

public sealed class ReplayEngine : IReplayEngine
{
    public TState Fold<TState>(TState seed, IEnumerable<TournamentEvent> events, Func<TState, TournamentEvent, TState> apply)
    {
        var acc = seed;
        var seen = new HashSet<long>();
        foreach (var e in events)
            if (seen.Add(e.EventId))
                acc = apply(acc, e);
        return acc;
    }
}
