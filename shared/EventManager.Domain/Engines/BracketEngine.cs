using ErrorOr;

namespace EventManager.Domain.Engines;

public interface IBracketEngine
{
    ErrorOr<Bracket> GenerateSingleElimination(Snowflake divisionId, IReadOnlyList<Seed> seeds, Func<Snowflake> nextId);
    ErrorOr<Bracket> GenerateRoundRobin(Snowflake divisionId, IReadOnlyList<Seed> seeds, Func<Snowflake> nextId);
    ErrorOr<Bracket> Advance(Bracket bracket, Snowflake matchId, MatchOutcome outcome);
}

/// <summary>
/// Pure bracket generation/advancement (FR-3.2/3.5, BR-3.x). Byes go to top seeds first (Q5=A).
/// Deterministic given inputs + <paramref name="nextId"/>.
/// </summary>
public sealed class BracketEngine : IBracketEngine
{
    public ErrorOr<Bracket> GenerateSingleElimination(Snowflake divisionId, IReadOnlyList<Seed> seeds, Func<Snowflake> nextId)
    {
        if (seeds.Count < 2) return Error.Validation("Bracket.TooFewAthletes", "A bracket needs at least 2 athletes.");

        var ordered = seeds.OrderBy(s => s.SeedNumber).ToList();
        int n = ordered.Count;
        int size = NextPow2(n);
        int[] positions = StandardSeedingOrder(size); // seed numbers (1..size) in position order

        Snowflake? CompetitorForSeedNo(int seedNo)
        {
            if (seedNo <= n) return ordered[seedNo - 1].RegistrationId;
            return null; // > n => Bye
        }

        var matches = new List<Match>();
        int rounds = Log2(size);

        // Round 0 from seeded positions.
        for (int slot = 0; slot < size / 2; slot++)
        {
            var a = CompetitorForSeedNo(positions[slot * 2]);
            var b = CompetitorForSeedNo(positions[slot * 2 + 1]);
            MatchOutcome? outcome = null;
            // A bye auto-resolves in favor of the present competitor.
            if (a is null && b is not null) outcome = new MatchOutcome(b, MatchMethod.Forfeit, "Bye");
            else if (b is null && a is not null) outcome = new MatchOutcome(a, MatchMethod.Forfeit, "Bye");
            matches.Add(new Match(nextId(), 0, slot, a, b, outcome));
        }

        // Empty later rounds.
        int slotsInRound = size / 2;
        for (int r = 1; r < rounds; r++)
        {
            slotsInRound /= 2;
            for (int slot = 0; slot < slotsInRound; slot++)
                matches.Add(new Match(nextId(), r, slot, null, null));
        }

        var bracket = new Bracket(nextId(), divisionId, BracketFormat.SingleElimination, ordered, matches, DivisionStatus.NotStarted);
        return PropagateByes(bracket);
    }

    public ErrorOr<Bracket> GenerateRoundRobin(Snowflake divisionId, IReadOnlyList<Seed> seeds, Func<Snowflake> nextId)
    {
        if (seeds.Count < 2) return Error.Validation("Bracket.TooFewAthletes", "A round-robin needs at least 2 athletes.");

        var ordered = seeds.OrderBy(s => s.SeedNumber).ToList();
        var matches = new List<Match>();
        int round = 0;
        for (int i = 0; i < ordered.Count; i++)
            for (int j = i + 1; j < ordered.Count; j++)
                matches.Add(new Match(nextId(), round, matches.Count, ordered[i].RegistrationId, ordered[j].RegistrationId));

        return new Bracket(nextId(), divisionId, BracketFormat.RoundRobin, ordered, matches, DivisionStatus.NotStarted);
    }

    public ErrorOr<Bracket> Advance(Bracket bracket, Snowflake matchId, MatchOutcome outcome)
    {
        var idx = FindMatchIndex(bracket, matchId);
        if (idx < 0) return Error.NotFound("Bracket.MatchNotFound", $"Match {matchId} is not in the bracket.");

        var match = bracket.Matches[idx];
        if (match.Outcome is not null && match.Outcome.Detail != "Bye")
            return Error.Conflict("Bracket.AlreadyDecided", "Match already has an outcome.");

        var matches = bracket.Matches.ToList();
        matches[idx] = match with { Outcome = outcome };

        if (bracket.Format == BracketFormat.SingleElimination && outcome.Winner is { } winner)
            PlaceWinnerInNextRound(matches, match, winner);

        return bracket with { Matches = matches };
    }

    // --- helpers ---

    private static ErrorOr<Bracket> PropagateByes(Bracket bracket)
    {
        var matches = bracket.Matches.ToList();
        foreach (var m in bracket.Matches.Where(m => m.RoundIndex == 0 && m.Outcome?.Detail == "Bye" && m.Outcome.Winner is not null))
            PlaceWinnerInNextRound(matches, m, m.Outcome!.Winner!.Value);
        return bracket with { Matches = matches };
    }

    private static void PlaceWinnerInNextRound(List<Match> matches, Match played, Snowflake winner)
    {
        int nextRound = played.RoundIndex + 1;
        int nextSlot = played.SlotIndex / 2;
        int ni = matches.FindIndex(m => m.RoundIndex == nextRound && m.SlotIndex == nextSlot);
        if (ni < 0) return; // final round; no next match
        var next = matches[ni];
        if (played.SlotIndex % 2 == 0)
            matches[ni] = next with { CompetitorA = winner };
        else
            matches[ni] = next with { CompetitorB = winner };
    }

    private static int FindMatchIndex(Bracket b, Snowflake matchId)
    {
        for (int i = 0; i < b.Matches.Count; i++)
            if (b.Matches[i].MatchId == matchId) return i;
        return -1;
    }

    internal static int NextPow2(int n) { int p = 1; while (p < n) p <<= 1; return p; }

    internal static int Log2(int size) { int l = 0; while ((1 << l) < size) l++; return l; }

    /// <summary>Standard tournament seeding order: seeds 1 and 2 land on opposite ends, etc.</summary>
    internal static int[] StandardSeedingOrder(int size)
    {
        var seeds = new List<int> { 1 };
        int len = 1;
        while (len < size)
        {
            var next = new List<int>();
            int sum = 2 * len + 1;
            foreach (var s in seeds) { next.Add(s); next.Add(sum - s); }
            seeds = next;
            len *= 2;
        }
        return seeds.ToArray();
    }
}
