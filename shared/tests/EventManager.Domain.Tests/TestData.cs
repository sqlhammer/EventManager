using EventManager.Domain;

namespace EventManager.Domain.Tests;

internal static class TestData
{
    public static IReadOnlyList<Seed> Seeds(int n, string? academy = null) =>
        Enumerable.Range(1, n)
            .Select(i => new Seed((Snowflake)(1000 + i), i, academy))
            .ToList();

    public static Func<Snowflake> Counter()
    {
        long c = 1;
        return () => (Snowflake)(c++);
    }

    public static Division DivisionWithUpper(double upper, DivisionStatus status = DivisionStatus.NotStarted, long id = 1) =>
        new(
            (Snowflake)id,
            (Snowflake)99,
            new DivisionCriteria(new WeightClass(null, upper), new RankRange(0, 10), new AgeRange(0, 99), "M"),
            BracketFormat.SingleElimination,
            status);
}
