// Adapted from KasNetWallet Core (Keystore/WalletKeystore.cs), same author, MIT License. Part of KaspaMy (MIT).
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;

namespace KaspaMy.Core.Keystore;

public sealed class WrongPasswordException : Exception
{
    public WrongPasswordException() : base("Wrong password or corrupted keystore.") { }
}

/// <summary>The wallet file is unreadable, tampered with, or in an unsupported format. Message is user-safe.</summary>
public sealed class KeystoreCorruptException : IOException
{
    private const string Tail = "Restore it from backup or re-import your recovery phrase.";
    public KeystoreCorruptException(string detail) : base(Compose(detail)) { }

    /// <summary>N5: never nest our own message inside itself.</summary>
    private static string Compose(string detail)
    {
        detail = (detail ?? "unreadable").Trim();
        if (detail.Contains(Tail, StringComparison.Ordinal)) return detail;
        return "The wallet file is corrupted or unsupported (" + detail.TrimEnd('.') + "). " + Tail;
    }
}

/// <summary>
/// Encrypts the BIP39 mnemonic with AES-256-GCM under a key from Argon2id(password, salt).
/// Associated data binds version, network and address. Byte buffers holding secrets are zeroed after use;
/// .NET strings (the mnemonic/password as typed) cannot be reliably zeroed — see docs/DECISIONS.md (D5).
/// </summary>
public static class WalletKeystore
{
    private const int SaltLen = 16, NonceLen = 12, TagLen = 16, KeyLen = 32;
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static KeystoreFile Encrypt(string mnemonic, string password, WalletNetwork network, KdfParameters? kdf = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        kdf ??= KdfParameters.Default;
        kdf.Validate();
        var normalized = MnemonicService.Normalize(mnemonic);
        var address = HdAccount.DeriveAddress(normalized, network);

        var file = new KeystoreFile
        {
            Argon2MemoryKiB = kdf.MemoryKiB,
            Argon2Iterations = kdf.Iterations,
            Argon2Parallelism = kdf.Parallelism,
            Network = network.ToString(),
            Address = address,
            CreatedUtc = DateTimeOffset.UtcNow,
        };
        var salt = RandomNumberGenerator.GetBytes(SaltLen);
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var plain = Encoding.UTF8.GetBytes(normalized);
        var key = DeriveKey(password, salt, kdf);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagLen];
        try
        {
            using var gcm = new AesGcm(key, TagLen);
            gcm.Encrypt(nonce, plain, cipher, tag, AssociatedData(file));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
        file.Salt = Convert.ToBase64String(salt);
        file.Nonce = Convert.ToBase64String(nonce);
        file.Ciphertext = Convert.ToBase64String(cipher);
        file.Tag = Convert.ToBase64String(tag);
        return file;
    }

    /// <summary>Returns the mnemonic. Caller must not log it and should drop the reference ASAP.</summary>
    public static string Decrypt(KeystoreFile file, string password)
    {
        if (file.Version != 1 || file.Kdf != "argon2id" || file.Cipher != "aes-256-gcm")
            throw new KeystoreCorruptException("unsupported format");
        var kdf = new KdfParameters(file.Argon2MemoryKiB, file.Argon2Iterations, file.Argon2Parallelism);
        kdf.Validate();
        byte[] salt, nonce, cipher, tag;
        try
        {
            salt = Convert.FromBase64String(file.Salt ?? ""); nonce = Convert.FromBase64String(file.Nonce ?? "");
            cipher = Convert.FromBase64String(file.Ciphertext ?? ""); tag = Convert.FromBase64String(file.Tag ?? "");
        }
        catch (FormatException) { throw new KeystoreCorruptException("bad encoding"); }
        if (salt.Length != SaltLen || nonce.Length != NonceLen || tag.Length != TagLen || cipher.Length == 0) throw new KeystoreCorruptException("bad field length");
        var plain = new byte[cipher.Length];
        var key = DeriveKey(password ?? "", salt, kdf);
        try
        {
            using var gcm = new AesGcm(key, TagLen);
            gcm.Decrypt(nonce, cipher, tag, plain, AssociatedData(file));
            return Encoding.UTF8.GetString(plain);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new WrongPasswordException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Decrypts and derives the account; also checks the stored address matches.</summary>
    public static HdAccount Unlock(KeystoreFile file, string password)
    {
        if (!Enum.TryParse<WalletNetwork>(file.Network, ignoreCase: false, out var network) || !Enum.IsDefined(network))
            throw new KeystoreCorruptException("unknown network");
        var mnemonic = Decrypt(file, password);
        var acc = HdAccount.FromMnemonic(mnemonic, network);
        if (acc.Address != file.Address)
        {
            acc.Dispose();
            throw new KeystoreCorruptException("address mismatch");
        }
        return acc;
    }

    public static void Save(KeystoreFile file, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        WriteOwnerOnly(tmp, JsonSerializer.Serialize(file, Opts));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Writes a file readable only by the current user on Linux/macOS (0600). Windows: inherits the %APPDATA% ACL.</summary>
    public static void WriteOwnerOnly(string path, string contents)
    {
        if (OperatingSystem.IsWindows()) { File.WriteAllText(path, contents); return; }
        var opts = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite };
        using (var fs = new FileStream(path, opts)) using (var w = new StreamWriter(fs)) w.Write(contents);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>Loads and structurally validates; any malformed content becomes <see cref="KeystoreCorruptException"/>.</summary>
    public static KeystoreFile Load(string path)
    {
        KeystoreFile? f;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 64 * 1024) throw new KeystoreCorruptException("file too large");
            f = JsonSerializer.Deserialize<KeystoreFile>(File.ReadAllText(path), Opts);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or FormatException or InvalidOperationException)
        { throw new KeystoreCorruptException("unreadable JSON"); }
        if (f is null) throw new KeystoreCorruptException("empty file");
        if (!Enum.TryParse<WalletNetwork>(f.Network, false, out var n) || !Enum.IsDefined(n)) throw new KeystoreCorruptException("unknown network");
        if (string.IsNullOrEmpty(f.Address)) throw new KeystoreCorruptException("missing address");
        new KdfParameters(f.Argon2MemoryKiB, f.Argon2Iterations, f.Argon2Parallelism).Validate();
        return f;
    }

    private static byte[] AssociatedData(KeystoreFile f) =>
        Encoding.UTF8.GetBytes($"kdw-keystore|v{f.Version}|{f.Network}|{f.Address}|{f.Argon2MemoryKiB}|{f.Argon2Iterations}|{f.Argon2Parallelism}");

    private static byte[] DeriveKey(string password, byte[] salt, KdfParameters kdf)
    {
        var pw = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon = new Argon2id(pw)
            {
                Salt = salt,
                MemorySize = kdf.MemoryKiB,
                Iterations = kdf.Iterations,
                DegreeOfParallelism = kdf.Parallelism,
            };
            return argon.GetBytes(KeyLen);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pw);
        }
    }
}
