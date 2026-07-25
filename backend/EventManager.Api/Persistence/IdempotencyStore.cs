using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace EventManager.Api.Persistence;

/// <summary>
/// Command idempotency (RP-2). A key's first result is recorded; a replay returns it instead of
/// re-executing — backs bulk-batch resubmit (BR-REG-7) and payment retry (BR-PAY-1/3). Checked and
/// written inside the caller's write transaction so the guarantee is durable.
/// </summary>
public sealed class IdempotencyStore(AppDbContext db)
{
    public async Task<T?> TryGetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        var row = await db.IdempotencyKeys.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
        return row is null ? null : JsonSerializer.Deserialize<T>(row.ResultJson);
    }

    public void Record<T>(string key, T result)
    {
        var json = JsonSerializer.Serialize(result);
        db.IdempotencyKeys.Add(new IdempotencyKey
        {
            Key = key,
            ResultHash = Hash(json),
            ResultJson = json,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}
