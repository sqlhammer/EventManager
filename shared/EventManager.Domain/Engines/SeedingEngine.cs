using ErrorOr;

namespace EventManager.Domain.Engines;

public interface ISeedingEngine
{
    ErrorOr<IReadOnlyList<Seed>> Seed(IReadOnlyList<Registration> registrations, SeedingOptions options);
}

/// <summary>
/// Random baseline seeding with academy separation across halves then quarters where the bracket
/// size permits (FR-3.3, Q5=A, BR-3.6). Deterministic given <see cref="SeedingOptions.RandomSeed"/>.
/// </summary>
public sealed class SeedingEngine : ISeedingEngine
{
    public ErrorOr<IReadOnlyList<Seed>> Seed(IReadOnlyList<Registration> registrations, SeedingOptions options)
    {
        if (registrations.Count < 2)
            return Error.Validation("Seeding.TooFew", "Need at least 2 registrations to seed.");

        // Deterministic shuffle (baseline randomness).
        var rng = new Random(options.RandomSeed);
        var shuffled = registrations.OrderBy(_ => rng.Next()).ToList();

        // Interleave by academy so same-academy athletes are spread across the ordering as far as
        // possible: round-robin drain from per-academy queues, largest academy first.
        var byAcademy = shuffled
            .GroupBy(r => r.Snapshot.Academy)
            .OrderByDescending(g => g.Count())
            .Select(g => new Queue<Registration>(g))
            .ToList();

        var order = new List<Registration>();
        while (order.Count < shuffled.Count)
            foreach (var q in byAcademy)
                if (q.Count > 0) order.Add(q.Dequeue());

        var seeds = order
            .Select((r, i) => new Seed(r.RegistrationId, i + 1, r.Snapshot.Academy))
            .ToList();

        return seeds;
    }
}
