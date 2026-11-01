// Adapted from KasNetWallet Core (Keystore/KeystoreFile.cs), same author, MIT License. Part of KaspaMy (MIT).
namespace KaspaMy.Core.Keystore;

/// <summary>On-disk JSON format (version 1). Contains no plaintext secret.</summary>
public sealed class KeystoreFile
{
    public int Version { get; set; } = 1;
    public string Kdf { get; set; } = "argon2id";
    public int Argon2MemoryKiB { get; set; }
    public int Argon2Iterations { get; set; }
    public int Argon2Parallelism { get; set; }
    public string Salt { get; set; } = "";
    public string Cipher { get; set; } = "aes-256-gcm";
    public string Nonce { get; set; } = "";
    public string Ciphertext { get; set; } = "";
    public string Tag { get; set; } = "";
    /// <summary>Public, informational. Bound into the AEAD associated data so it cannot be swapped.</summary>
    public string Network { get; set; } = "";
    public string Address { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
}
