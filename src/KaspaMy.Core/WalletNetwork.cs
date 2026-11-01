// Adapted from KasNetWallet Core (WalletNetwork.cs), same author, MIT License. Part of KaspaMy (MIT).
using NKaspa;

namespace KaspaMy.Core;

public enum WalletNetwork
{
    Mainnet,
    Testnet10,
}

public static class WalletNetworkExtensions
{
    public const WalletNetwork Default = WalletNetwork.Testnet10;

    public static Network ToNKaspa(this WalletNetwork n) => n switch
    {
        WalletNetwork.Mainnet => Network.Mainnet,
        WalletNetwork.Testnet10 => Network.Testnet,
        _ => throw new ArgumentOutOfRangeException(nameof(n)),
    };

    /// <summary>Network id as reported by getServerInfo and used in file names ("mainnet", "testnet-10").</summary>
    public static string ExpectedNodeNetworkId(this WalletNetwork n) => n switch
    {
        WalletNetwork.Mainnet => "mainnet",
        WalletNetwork.Testnet10 => "testnet-10",
        _ => throw new ArgumentOutOfRangeException(nameof(n)),
    };

    public static string Id(this WalletNetwork n) => n.ExpectedNodeNetworkId();

    /// <summary>Accepts mainnet | testnet-10 | testnet10 | tn10 | testnet (case-insensitive).</summary>
    public static bool TryParse(string? s, out WalletNetwork n)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "mainnet": case "main": n = WalletNetwork.Mainnet; return true;
            case "testnet-10": case "testnet10": case "tn10": case "testnet": n = WalletNetwork.Testnet10; return true;
            default: n = Default; return false;
        }
    }

    public static WalletNetwork Parse(string? s) =>
        TryParse(s, out var n) ? n : throw new ArgumentException($"Unknown network '{s}'. Use testnet-10 or mainnet.");
}
