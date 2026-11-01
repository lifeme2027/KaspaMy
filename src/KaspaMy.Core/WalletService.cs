// KaspaMy (MIT). Copyright (c) 2026 jun chen.
// Keystore format and crypto follow KasNetWallet (AES-256-GCM + Argon2id); see Keystore/WalletKeystore.cs.
using KaspaMy.Core.Keystore;

namespace KaspaMy.Core;

/// <summary>Public, secret-free wallet description. Safe to print, return from MCP tools, or log.</summary>
public sealed record WalletInfo(string Network, string Address, string DerivationPath, string KeystorePath, DateTimeOffset CreatedUtc);

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
        if (File.Exists(path)) throw new WalletExistsException(path);
        var file = WalletKeystore.Encrypt(mnemonic, password, n, _kdf);
        WalletKeystore.Save(file, path);
        WalletLog.Info($"wallet stored: network={n.Id()} address={file.Address}");
        return ToInfo(file, path);
    }

    public static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw new ArgumentException($"Password must be at least {MinPasswordLength} characters.");
    }

    /// <summary>Public info from the file, no password needed (address is stored in clear and bound into the AEAD data).</summary>
    public static WalletInfo Status(WalletNetwork n)
    {
        var path = KaspaMyPaths.WalletPath(n);
        if (!File.Exists(path)) throw new WalletNotFoundException(n);
        return ToInfo(WalletKeystore.Load(path), path);
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

    private static WalletInfo ToInfo(KeystoreFile f, string path) =>
        new(Enum.Parse<WalletNetwork>(f.Network).Id(), f.Address, HdAccount.ReceivePath, path, f.CreatedUtc);
}
