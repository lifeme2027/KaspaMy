// Adapted from KasNetWallet Core (Rpc/IWalletNode.cs), same author, MIT License. Part of KaspaMy (MIT).
using NKaspa;
using NKaspa.RPC;

namespace KaspaMy.Core.Rpc;

public sealed record FeeRates(double? Priority, double? Normal, double? Low);

public sealed record NodeStatus(string ServerVersion, string NetworkId, bool IsSynced, bool HasUtxoIndex, ulong VirtualDaaScore);

/// <summary>Thin wallet-facing wrapper over NKaspa's <see cref="IKaspaRpcClient"/>.</summary>
public interface IWalletNode : IAsyncDisposable
{
    Task<NodeStatus> GetStatusAsync(CancellationToken ct = default);
    Task<Money> GetBalanceAsync(string address, CancellationToken ct = default);
    Task<IReadOnlyList<Coin>> GetCoinsAsync(string address, CancellationToken ct = default);
    /// <summary>Normal-bucket fee rate in sompi/gram, or null if the node does not report one.</summary>
    Task<double?> GetNormalFeeRateAsync(CancellationToken ct = default);
    /// <summary>KaspaMy: full fee estimate (priority / normal / low feerates in sompi/gram).</summary>
    Task<FeeRates> GetFeeRatesAsync(CancellationToken ct = default);
    Task<string> SubmitAsync(Transaction tx, CancellationToken ct = default);
}
