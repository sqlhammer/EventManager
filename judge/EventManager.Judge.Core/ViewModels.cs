namespace EventManager.Judge.Core;

/// <summary>A match awaiting scoring on the judge's assigned mat.</summary>
public sealed record QueuedMatch(long DivisionId, long MatchId, long? CompetitorA, long? CompetitorB);

/// <summary>
/// Assigned-mat match queue (US-401). Populated/advanced from hub pushes. Read model only — scoring
/// goes through <see cref="ScoreCaptureService"/>.
/// </summary>
public sealed class MatQueueViewModel
{
    private readonly List<QueuedMatch> _queue = [];

    public IReadOnlyList<QueuedMatch> Queue => _queue;

    public QueuedMatch? Current
    {
        get
        {
            if (_queue.Count == 0) return null;
            return _queue[0];
        }
    }

    public void Enqueue(QueuedMatch match) => _queue.Add(match);

    /// <summary>Mark the current match done (hub confirmed advancement) and move to the next.</summary>
    public void CompleteCurrent()
    {
        if (_queue.Count > 0) _queue.RemoveAt(0);
    }

    /// <summary>Replace the queue from a fresh hub push (US-401 keeps the queue current).</summary>
    public void Replace(IEnumerable<QueuedMatch> matches)
    {
        _queue.Clear();
        _queue.AddRange(matches);
    }
}

/// <summary>
/// Read-only cross-mat view (US-410). Surfaces other mats' state; it exposes no write path, so a judge
/// can watch another mat but never score it.
/// </summary>
public sealed class CrossMatViewModel
{
    private readonly Dictionary<long, IReadOnlyList<QueuedMatch>> _byMat = new();

    public IReadOnlyList<QueuedMatch> ForMat(long matDivisionId)
    {
        if (_byMat.TryGetValue(matDivisionId, out var matches)) return matches;
        return [];
    }

    public void Update(long matDivisionId, IReadOnlyList<QueuedMatch> matches) => _byMat[matDivisionId] = matches;
}

/// <summary>Match focus/lock mode (US-411): while locked, the UI pins the active match and ignores
/// stray taps outside it.</summary>
public sealed class FocusModeState
{
    public bool IsLocked { get; private set; }
    public long? LockedMatchId { get; private set; }

    public void Lock(long matchId)
    {
        IsLocked = true;
        LockedMatchId = matchId;
    }

    public void Unlock()
    {
        IsLocked = false;
        LockedMatchId = null;
    }
}
