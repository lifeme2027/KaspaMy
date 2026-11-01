// KaspaMy (MIT). Copyright (c) 2026 jun chen.
using NKaspa;

namespace KaspaMy.Core;

public sealed record AddressValidation(bool Valid, string? Network, string? Type, bool MatchesExpectedNetwork, string? Error);

public static class AddressValidator
{
    /// <summary>Lower-cases an all-uppercase address (F10); otherwise returns it trimmed.</summary>
    public static string Normalize(string address)
    {
        var a = address.Trim();
        return a == a.ToUpperInvariant() ? a.ToLowerInvariant() : a;
    }

    /// <summary>Validates a Kaspa address (bech32 checksum, prefix, version) with NKaspa.</summary>
    public static AddressValidation Validate(string? address, WalletNetwork? expected = null)
    {
        var a = address?.Trim();
        if (string.IsNullOrEmpty(a)) return new(false, null, null, false, "Address is empty.");
        // F10: BIP173/CashAddr allow all-uppercase; mixed case stays invalid.
        if (a == a.ToUpperInvariant() && a != a.ToLowerInvariant()) a = a.ToLowerInvariant();
        if (!KaspaAddress.TryParse(a, out var parsed) || parsed is null) return new(false, null, null, false, "Not a valid Kaspa address (checksum, prefix or format).");
        string? net = parsed.Network == Network.Mainnet ? "mainnet" : parsed.Network == Network.Testnet ? "testnet-10" : parsed.Network.ToString();
        var type = parsed.Version.ToString();
        var matches = expected is null || parsed.Network == expected.Value.ToNKaspa();
        return new(true, net, type, matches, matches ? null : $"Address is for {net}, not {expected!.Value.Id()}.");
    }
}
