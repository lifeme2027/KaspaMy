// Adapted from KasNetWallet Core (MassPolicy.cs), same author, MIT License. Part of KaspaMy (MIT).
using System.Text.Json.Serialization;
using NKaspa;
using NKaspa.Policy;
using NkMassPolicy = NKaspa.Policy.MassPolicy;

namespace KaspaMy.Core;

/// <summary>
/// Wallet-side transaction mass policy (per network, settings.json). Since NKaspa 5c49251 (pin dea2697) the actual rules come from
/// <see cref="NkMassPolicy"/> (<c>Network.MassPolicyAt(daaScore)</c>):
/// <list type="bullet">
/// <item>ToccataActive = null (default, "auto"): decided from the node's virtual DAA score (<see cref="VirtualDaaScore"/>).
/// Without a DAA score: testnet → post-Toccata (testnet-10 has activated); mainnet → conservative mix (pre-Toccata 100,000 cap,
/// but 100 sompi/gram relay rate, so neither the mass nor the fee can be too low under either rule set).</item>
/// <item>true / false: forced post- / pre-Toccata NKaspa policy.</item>
/// </list>
/// MaxTransactionMass, if set, further lowers the cap (wallet-side guard).
/// </summary>
public sealed class MassPolicy
{
    public bool? ToccataActive { get; set; }
    public ulong? MaxTransactionMass { get; set; }

    /// <summary>Runtime only: the node's virtual DAA score, used when <see cref="ToccataActive"/> is null.</summary>
    [JsonIgnore] public ulong? VirtualDaaScore { get; set; }

    public static MassPolicy DefaultFor(WalletNetwork n) => new();

    public MassPolicy WithDaaScore(ulong? daa) => new() { ToccataActive = ToccataActive, MaxTransactionMass = MaxTransactionMass, VirtualDaaScore = daa };

    /// <summary>
    /// NKaspa dea2697: a pre-Toccata NKaspa policy (forced, or a DAA below activation) charges 1 sompi/gram, which current
    /// (post-Toccata) nodes may reject. The wallet therefore:
    /// <list type="number">
    /// <item>with a known DAA at/after activation always uses <see cref="NkMassPolicy.PostToccata"/>, even if ToccataActive = false is forced;</item>
    /// <item>never returns a relay rate below <see cref="NkMassPolicy.PostToccataMinimumRelayFeeRate"/> (100), whatever the cap.</item>
    /// </list>
    /// The Toccata activation score is the network's (testnet-10: <see cref="Network.TestnetToccataActivationDaaScore"/>);
    /// <see cref="Network.TestnetZkHardeningActivationDaaScore"/> is a different checkpoint and deliberately not used as the switch.
    /// </summary>
    public NkMassPolicy Resolve(Network network)
    {
        if (VirtualDaaScore is ulong daa && daa > 0 && network.IsToccataActive(daa) && ToccataActive != true) return NkMassPolicy.PostToccata;
        if (ToccataActive == true) return NkMassPolicy.PostToccata;
        if (ToccataActive == false || (VirtualDaaScore is ulong d && d > 0) || network == Network.Mainnet) return PreCapPostRate;
        return NkMassPolicy.PostToccata;
    }

    /// <summary>Pre-Toccata 100,000 mass cap with the post-Toccata 100 sompi/gram relay rate: neither mass nor fee too low under either rule set.</summary>
    internal static NkMassPolicy PreCapPostRate
    {
        get
        {
            var pre = NkMassPolicy.PreToccata;
            return new NkMassPolicy(false, pre.BlockLimits, pre.MaximumTransactionMass, NkMassPolicy.PostToccataMinimumRelayFeeRate);
        }
    }

    public ulong EffectiveCap(Network network)
    {
        var cap = Resolve(network).MaximumTransactionMass;
        return MaxTransactionMass is ulong m && m < cap ? m : cap;
    }

    public TransactionBuilder CreateBuilder(Network network)
    {
        var b = new TransactionBuilder(network, Resolve(network));
        b.MaximumMass = EffectiveCap(network);
        return b;
    }
}
