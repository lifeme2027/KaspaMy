// Adapted from KasNetWallet Core (Keystore/KdfParameters.cs), same author, MIT License. Part of KaspaMy (MIT).
namespace KaspaMy.Core.Keystore;

public sealed record KdfParameters(int MemoryKiB, int Iterations, int Parallelism)
{
    /// <summary>Default for real wallets: Argon2id, 64 MiB, t=3, p=4 (OWASP-style baseline).</summary>
    public static KdfParameters Default { get; } = new(64 * 1024, 3, 4);

    /// <summary>Fast parameters for unit tests only.</summary>
    public static KdfParameters TestOnly { get; } = new(1024, 1, 1);

    public const int MaxMemoryKiB = 1024 * 1024; // 1 GiB
    public const int MaxIterations = 10;
    public const int MaxParallelism = 16;

    public void Validate()
    {
        // Caps also bound the work a tampered file can force before the GCM tag is checked (KDW-08).
        if (MemoryKiB < 1024 || MemoryKiB > MaxMemoryKiB) throw new KeystoreCorruptException("Argon2 memory out of range.");
        if (Iterations < 1 || Iterations > MaxIterations) throw new KeystoreCorruptException("Argon2 iterations out of range.");
        if (Parallelism < 1 || Parallelism > MaxParallelism) throw new KeystoreCorruptException("Argon2 parallelism out of range.");
    }
}
