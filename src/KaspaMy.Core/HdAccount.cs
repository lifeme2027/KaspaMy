// Adapted from KasNetWallet Core (HdAccount.cs), same author, MIT License. Part of KaspaMy (MIT).
using NKaspa;

namespace KaspaMy.Core;

/// <summary>
/// Derived key material for the single MVP receive address m/44'/111111'/0'/0/0.
/// Holds a private <see cref="Key"/>; dispose (lock) to zero it.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(RefuseSecretJsonConverter<HdAccount>))]
public sealed class HdAccount : IDisposable
{
    /// <summary>Never exposes key material.</summary>
    public override string ToString() => $"HdAccount({Network}, {Address}) {SecretRedactor.Mask}";

    public static HdAccount FromMnemonic(SecretText mnemonic, WalletNetwork network, SecretText? bip39Passphrase = null) =>
        FromMnemonic(mnemonic.Reveal(), network, bip39Passphrase?.Reveal());

    public const string ReceivePath = "m/44'/111111'/0'/0/0";

    private Key? _key;

    private HdAccount(Key key, WalletNetwork network)
    {
        _key = key;
        Network = network;
        Address = key.GetAddress(network.ToNKaspa()).ToString();
    }

    public WalletNetwork Network { get; }
    public string Address { get; }

    internal Key Key => _key ?? throw new ObjectDisposedException(nameof(HdAccount));

    public static HdAccount FromMnemonic(string mnemonic, WalletNetwork network, string? bip39Passphrase = null)
    {
        var m = Mnemonic.Parse(MnemonicService.Normalize(mnemonic), Wordlist.English); // checksum enforced by NKaspa (and by Normalize)
        using var master = m.DeriveExtKey(bip39Passphrase);
        using var receive = master.Derive(KeyPath.KaspaReceive(0));
        return new HdAccount(receive.PrivateKey, network);
    }

    /// <summary>Address only, without keeping the key alive.</summary>
    public static string DeriveAddress(string mnemonic, WalletNetwork network, string? bip39Passphrase = null)
    {
        using var acc = FromMnemonic(mnemonic, network, bip39Passphrase);
        return acc.Address;
    }

    public void Dispose()
    {
        _key?.Dispose();
        _key = null;
    }
}
