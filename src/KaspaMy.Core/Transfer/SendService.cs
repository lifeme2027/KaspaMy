// KaspaMy (MIT). Copyright (c) 2026 jun chen.
// Flow follows KasNetWallet WalletManager: status check (network, synced, DAA) → coins → fee rate → build+sign+verify → confirm → submit.
using KaspaMy.Core.Rpc;
using NKaspa;

namespace KaspaMy.Core.Transfer;

public sealed record FeeEstimate(string Network, double? PriorityFeerate, double? NormalFeerate, double? LowFeerate,
    double AppliedFeerate, ulong Mass, ulong FeeSompi, string FeeKas, bool Exact, string Basis);

public static class SendService
{
    /// <summary>Approximate relay mass of a 1-input, 2-output Schnorr P2PK transfer (observed on tn10 by KasNetWallet: 0.002036 KAS at 100 sompi/gram).</summary>
    public const ulong TypicalMass = 2036;
    public const double FloorFeerate = 100;

    public static async Task<NodeStatus> RequireReadyAsync(IWalletNode node, WalletNetwork n, CancellationToken ct)
    {
        var st = await node.GetStatusAsync(ct).ConfigureAwait(false);
        if (!string.Equals(st.NetworkId, n.ExpectedNodeNetworkId(), StringComparison.OrdinalIgnoreCase))
            throw new TransferException($"Node network '{st.NetworkId}' does not match wallet network '{n.Id()}'.");
        if (!st.IsSynced) throw new TransferException("Node is not synced.");
        if (!st.HasUtxoIndex) throw new TransferException("Node has no UTXO index (--utxoindex); it cannot report balances or coins.");
        return st;
    }

    public static async Task<TransferPlan> PrepareAsync(IWalletNode node, HdAccount account, string to, ulong amountSompi, CancellationToken ct = default)
    {
        var st = await RequireReadyAsync(node, account.Network, ct).ConfigureAwait(false);
        var coins = await node.GetCoinsAsync(account.Address, ct).ConfigureAwait(false);
        var rate = await node.GetNormalFeeRateAsync(ct).ConfigureAwait(false);
        var mp = MassPolicy.DefaultFor(account.Network).WithDaaScore(st.VirtualDaaScore);
        return TransferService.Build(account, coins, to, amountSompi, FeeMode.NodeEstimate, rate, null, mp);
    }

    /// <summary>Approximate fee for a typical transfer at the node's normal rate (no wallet or password needed).</summary>
    public static async Task<FeeEstimate> EstimateAsync(IWalletNode node, WalletNetwork n, CancellationToken ct = default)
    {
        var r = await node.GetFeeRatesAsync(ct).ConfigureAwait(false);
        var applied = Math.Max(FloorFeerate, Math.Ceiling(r.Normal ?? r.Priority ?? FloorFeerate));
        var fee = (ulong)(applied * TypicalMass);
        return new(n.Id(), r.Priority, r.Normal, r.Low, applied, TypicalMass, fee, Kas.Format(fee), false,
            "typical 1-input/2-output transfer at max(node normal feerate, 100 sompi/gram)");
    }

    public static FeeEstimate FromPlan(TransferPlan p, FeeRates r) =>
        new(p.Network.Id(), r.Priority, r.Normal, r.Low, p.Mass == 0 ? 0 : (double)p.FeeSompi / p.Mass, p.Mass, p.FeeSompi, Kas.Format(p.FeeSompi), true,
            "exact: built and signed transaction from this wallet's UTXOs (not broadcast)");
}
