using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EventManager.Contracts;
using EventManager.Sync;

namespace EventManager.Hub.Resilience;

/// <summary>Encrypted backup envelope: integrity hash over the serialized event list (US-505).</summary>
public sealed record BackupContainer(string Sha256, string DataBase64);

public sealed record RecoveryResult(int EventsRestored);

/// <summary>
/// Hub local backup export (US-505/FR-4.9). Serializes the full event log to an AES-encrypted,
/// SHA-256 integrity-checked snapshot. The at-rest device DB encryption (SQLCipher) is separate.
/// </summary>
public sealed class BackupService
{
    public async Task<byte[]> ExportAsync(IEventStore source, string passphrase, CancellationToken ct = default)
    {
        var envelopes = new List<EventEnvelopeDto>();
        await foreach (var evt in source.ReadAllAsync(null, ct))
            envelopes.Add(EventEnvelopeMapper.ToDto(evt));

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(envelopes);
        var hash = Convert.ToHexString(SHA256.HashData(plaintext));
        var container = new BackupContainer(hash, Convert.ToBase64String(plaintext));
        var containerBytes = JsonSerializer.SerializeToUtf8Bytes(container);

        return BackupCrypto.Encrypt(containerBytes, passphrase);
    }
}

/// <summary>
/// Manual hub recovery (US-506/FR-4.8). Decrypts + integrity-verifies a snapshot and rebuilds state by
/// idempotent replay into a target store — replays never duplicate, so recovery is safe to re-run.
/// </summary>
public sealed class RecoveryService
{
    public async Task<RecoveryResult> RestoreAsync(byte[] snapshot, string passphrase, IEventStore target,
        Func<Task> saveChanges, CancellationToken ct = default)
    {
        var containerBytes = BackupCrypto.Decrypt(snapshot, passphrase);
        var container = JsonSerializer.Deserialize<BackupContainer>(containerBytes)
            ?? throw new InvalidOperationException("Corrupt backup container.");

        var plaintext = Convert.FromBase64String(container.DataBase64);
        var actualHash = Convert.ToHexString(SHA256.HashData(plaintext));
        if (!string.Equals(actualHash, container.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Backup integrity check failed.");

        var envelopes = JsonSerializer.Deserialize<List<EventEnvelopeDto>>(plaintext) ?? [];
        var restored = 0;
        foreach (var dto in envelopes)
        {
            if (await target.AppendIfNotExistsAsync(EventEnvelopeMapper.ToEvent(dto), ct)) restored++;
        }
        await saveChanges();
        return new RecoveryResult(restored);
    }
}

/// <summary>AES-CBC with a PBKDF2-derived key. Output layout: salt(16) || iv(16) || ciphertext.</summary>
internal static class BackupCrypto
{
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int Iterations = 100_000;

    public static byte[] Encrypt(byte[] plaintext, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        using var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256);
        using var aes = Aes.Create();
        aes.Key = kdf.GetBytes(32);
        aes.IV = RandomNumberGenerator.GetBytes(IvSize);

        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

        var output = new byte[SaltSize + IvSize + ciphertext.Length];
        Buffer.BlockCopy(salt, 0, output, 0, SaltSize);
        Buffer.BlockCopy(aes.IV, 0, output, SaltSize, IvSize);
        Buffer.BlockCopy(ciphertext, 0, output, SaltSize + IvSize, ciphertext.Length);
        return output;
    }

    public static byte[] Decrypt(byte[] input, string passphrase)
    {
        var salt = input[..SaltSize];
        var iv = input[SaltSize..(SaltSize + IvSize)];
        var ciphertext = input[(SaltSize + IvSize)..];

        using var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256);
        using var aes = Aes.Create();
        aes.Key = kdf.GetBytes(32);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    }
}
