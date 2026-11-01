// KaspaMy (MIT). Copyright (c) 2026 jun chen.
// Keystore format and crypto follow KasNetWallet (AES-256-GCM + Argon2id); see Keystore/WalletKeystore.cs.
using KaspaMy.Core.Keystore;

namespace KaspaMy.Core;

/// <summary>Public, secret-free wallet description. Safe to print, return from MCP tools, or log.</summary>
/// <param name="AddressVerified">True when the address was re-derived from the decrypted keystore (F2). False means it was read
/// from the file's plaintext header without a password and must not be used as a receive address.</param>
public sealed record WalletInfo(string Network, string Address, string DerivationPath, string KeystorePath, DateTimeOffset CreatedUtc, bool AddressVerified = false);

public sealed class WalletExistsException : InvalidOperationException
{
    public WalletExistsException(string path) : base($"A wallet already exists at {path}. Remove or back it up first (KaspaMy never overwrites a wallet).") { }
}

public sealed class WalletNotFoundException : InvalidOperationException
{
    public WalletNotFoundException(WalletNetwork n) : base($"No {n.Id()} wallet found at {KaspaMyPaths.WalletPath(n)}. Run 'kaspamy create' or 'kaspamy import' first.") { }
}

/// <summary>Create / import / open wallets. No method returns a mnemonic or private key except <see cref="ExportMnemonic"/>.</summary>
public sealed class WalletService
{
    public const string PasswordEnv = "KASPAMY_PASSWORD";
    public const int MinPasswordLength = 8;
    private readonly KdfParameters _kdf;

    public WalletService(KdfParameters? kdf = null) => _kdf = kdf ?? KdfParameters.Default;

    public static bool Exists(WalletNetwork n) => File.Exists(KaspaMyPaths.WalletPath(n));

    public WalletInfo Create(WalletNetwork n, string password, int words = 24)
    {
        var m = MnemonicService.Generate(words);
        return Store(n, m, password);
    }

    public WalletInfo Import(WalletNetwork n, string mnemonic, string password)
    {
        string normalized;
        try { normalized = MnemonicService.Normalize(mnemonic); }
        catch (FormatException ex) { throw new FormatException(SecretRedactor.Redact(ex.Message)); }
        return Store(n, normalized, password);
    }

    private WalletInfo Store(WalletNetwork n, string mnemonic, string password)
    {
        ValidatePassword(password);
        var path = KaspaMyPaths.WalletPath(n);
        // F1: lock + check + atomic no-overwrite create. The address returned is the one derived for the file we wrote.
        using var _ = WalletLock.Acquire(path);
        CleanStaleTemps(path);
        if (File.Exists(path)) throw new WalletExistsException(path);
        var file = WalletKeystore.Encrypt(mnemonic, password, n, _kdf);
        try { WalletKeystore.SaveNew(file, path); }
        catch (IOException) when (File.Exists(path)) { throw new WalletExistsException(path); }
        WalletLog.Info($"wallet stored: network={n.Id()} address={file.Address}");
        return ToInfo(file, path, verified: true);
    }

    /// <summary>N6: removes leftover temp files from crashed writers (older than 10 min). Called under the wallet lock.</summary>
    internal static void CleanStaleTemps(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir, Path.GetFileName(path) + ".*.tmp"))
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromMinutes(10)) File.Delete(f);
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw new ArgumentException($"Password must be at least {MinPasswordLength} characters.");
    }

    /// <summary>
    /// Header info WITHOUT a password. The address is NOT verified (F2): anyone with write access could have replaced it.
    /// Use <see cref="Info"/> before showing an address to receive funds.
    /// </summary>
    public static WalletInfo Status(WalletNetwork n)
    {
        var path = KaspaMyPaths.WalletPath(n);
        if (!File.Exists(path)) throw new WalletNotFoundException(n);
        return ToInfo(WalletKeystore.Load(path), path);
    }

    /// <summary>F2: decrypts, re-derives the address and checks it against the file. The only way to get a trusted address.</summary>
    public static WalletInfo Info(WalletNetwork n, string password)
    {
        var path = KaspaMyPaths.WalletPath(n);
        if (!File.Exists(path)) throw new WalletNotFoundException(n);
        var f = WalletKeystore.Load(path);
        using var acc = WalletKeystore.Unlock(f, password);
        return ToInfo(f, path, verified: true) with { Address = acc.Address };
    }

    /// <summary>Decrypts and derives the signing key. Dispose the result promptly.</summary>
    public static HdAccount Unlock(WalletNetwork n, string password)
    {
        var path = KaspaMyPaths.WalletPath(n);
        if (!File.Exists(path)) throw new WalletNotFoundException(n);
        return WalletKeystore.Unlock(WalletKeystore.Load(path), password);
    }

    /// <summary>The ONLY path that reveals the mnemonic. Used solely by the CLI export-mnemonic command after typed confirmation.</summary>
    public static SecretText ExportMnemonic(WalletNetwork n, string password)
    {
        var path = KaspaMyPaths.WalletPath(n);
        if (!File.Exists(path)) throw new WalletNotFoundException(n);
        return new SecretText(WalletKeystore.Decrypt(WalletKeystore.Load(path), password));
    }

    private static WalletInfo ToInfo(KeystoreFile f, string path, bool verified = false) =>
        new(Enum.Parse<WalletNetwork>(f.Network).Id(), f.Address, HdAccount.ReceivePath, path, f.CreatedUtc, verified);
}
