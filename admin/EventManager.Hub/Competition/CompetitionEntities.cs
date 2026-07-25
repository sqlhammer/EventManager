using EventManager.Domain;

namespace EventManager.Hub.Competition;

// --- Read-model rows folded from competition events (U4b) ---

public sealed class BracketRow
{
    public long DivisionId { get; set; }     // PK
    public long EventId { get; set; }
    public string Format { get; set; } = nameof(BracketFormat.SingleElimination);
    public string Status { get; set; } = nameof(DivisionStatus.NotStarted);
    public string MatchesJson { get; set; } = "[]";   // serialized MatchDto[]
}

public sealed class StandingRow
{
    public long Id { get; set; }
    public long DivisionId { get; set; }
    public long RegistrationId { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int? Placement { get; set; }
}

public sealed class DisputeRow
{
    public long DisputeId { get; set; }
    public long DivisionId { get; set; }
    public long MatchId { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "Open";   // Open | Resolved
    public string? Resolution { get; set; }
}

public sealed class DivisionStatusRow
{
    public long DivisionId { get; set; }    // PK
    public long EventId { get; set; }
    public int Registered { get; set; }
    public int CheckedIn { get; set; }
    public int Weighed { get; set; }
    public int Cleared { get; set; }
    public string Status { get; set; } = nameof(DivisionStatus.NotStarted);
}

/// <summary>Flat, serialization-friendly match shape (avoids serializing Snowflake structs).</summary>
public sealed record MatchDto(long MatchId, int RoundIndex, int SlotIndex, long? CompetitorA, long? CompetitorB,
    long? WinnerId, string? Method, string? Detail);

/// <summary>Maps between the U1 <see cref="Bracket"/> match list and the stored <see cref="MatchDto"/> list.</summary>
public static class BracketMapper
{
    public static IReadOnlyList<MatchDto> ToDtos(IReadOnlyList<Match> matches) => matches.Select(m => new MatchDto(
        m.MatchId.Value, m.RoundIndex, m.SlotIndex,
        m.CompetitorA?.Value, m.CompetitorB?.Value,
        m.Outcome?.Winner?.Value, m.Outcome?.Method.ToString(), m.Outcome?.Detail)).ToList();

    public static IReadOnlyList<Match> ToMatches(IEnumerable<MatchDto> dtos)
    {
        var matches = new List<Match>();
        foreach (var d in dtos)
        {
            Snowflake? competitorA = null;
            if (d.CompetitorA is { } a) competitorA = (Snowflake)a;

            Snowflake? competitorB = null;
            if (d.CompetitorB is { } b) competitorB = (Snowflake)b;

            MatchOutcome? outcome = null;
            if (d.WinnerId is { } w && d.Method is { } method)
                outcome = new MatchOutcome((Snowflake)w, Enum.Parse<MatchMethod>(method), d.Detail);

            matches.Add(new Match((Snowflake)d.MatchId, d.RoundIndex, d.SlotIndex, competitorA, competitorB, outcome));
        }
        return matches;
    }
}
