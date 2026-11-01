// Adapted from KasNetWallet Core (Rpc/WrpcWalletNode.cs), same author, MIT License. Part of KaspaMy (MIT).
using NKaspa;
using NKaspa.RPC;

namespace KaspaMy.Core.Rpc;

/// <summary>wRPC (JSON over WebSocket) node connection using NKaspa's KaspaWebSocketChannel + KaspaWrpcJsonClient.</summary>
public sealed class WrpcWalletNode : IWalletNode
{
    private readonly KaspaWebSocketChannel? _channel;
    private readonly IKaspaRpcClient _rpc;

    private WrpcWalletNode(KaspaWebSocketChannel? channel, IKaspaRpcClient rpc)
    {
        _channel = channel;
        _rpc = rpc;
    }

    /// <summary>For tests: wrap any NKaspa RPC client (e.g. over a canned channel).</summary>
    public static WrpcWalletNode FromClient(IKaspaRpcClient rpc) => new(null, rpc);

    /// <summary>Wallet cap on one wRPC frame (NKaspa 5c49251 default is also 16 MiB; set explicitly as a guard).</summary>
    public const int MaxMessageBytes = 16 * 1024 * 1024;

    public static async Task<WrpcWalletNode> ConnectAsync(string url, CancellationToken ct = default, TimeSpan? timeout = null)
    {
        // NKaspa 5c49251 options: frame size cap and timeouts. AutoReconnect stays off: ResilientWalletNode owns
        // reconnect/failover so that a reconnect is always followed by a network-id re-check.
        var options = new KaspaWebSocketOptions { MaxMessageBytes = MaxMessageBytes, AutoReconnect = false };
        if (timeout is TimeSpan t && t > TimeSpan.Zero) { options.ConnectTimeout = t; options.RequestTimeout = t; }
        var channel = new KaspaWebSocketChannel(new Uri(url), options);
        try
        {
            await channel.ConnectAsync(ct).ConfigureAwait(false);
            return new WrpcWalletNode(channel, new KaspaWrpcJsonClient(channel));
        }
        catch
        {
            await channel.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<NodeStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var i = await _rpc.GetServerInfoAsync(ct).ConfigureAwait(false);
        return new NodeStatus(i.ServerVersion, i.NetworkId, i.IsSynced, i.HasUtxoIndex, i.VirtualDaaScore);
    }

    public Task<Money> GetBalanceAsync(string address, CancellationToken ct = default) => _rpc.GetBalanceByAddressAsync(address, ct);

    public async Task<IReadOnlyList<Coin>> GetCoinsAsync(string address, CancellationToken ct = default)
    {
        var list = await _rpc.GetUtxosByAddressesAsync(new[] { address }, ct).ConfigureAwait(false);
        return list.Select(u => new Coin(u.Outpoint, u.Entry)).ToList();
    }

    public async Task<double?> GetNormalFeeRateAsync(CancellationToken ct = default)
    {
        var est = await _rpc.GetFeeEstimateAsync(ct).ConfigureAwait(false);
        if (est.NormalBuckets.Count > 0) return est.NormalBuckets[0].Feerate;
        return est.PriorityBucket?.Feerate;
    }

    public async Task<FeeRates> GetFeeRatesAsync(CancellationToken ct = default)
    {
        var est = await _rpc.GetFeeEstimateAsync(ct).ConfigureAwait(false);
        return new FeeRates(est.PriorityBucket?.Feerate,
            est.NormalBuckets.Count > 0 ? est.NormalBuckets[0].Feerate : null,
            est.LowBuckets.Count > 0 ? est.LowBuckets[0].Feerate : null);
    }

    public async Task<string> SubmitAsync(Transaction tx, CancellationToken ct = default) =>
        (await _rpc.SubmitTransactionAsync(tx, false, ct).ConfigureAwait(false)).ToString();

    public ValueTask DisposeAsync() => _channel?.DisposeAsync() ?? ValueTask.CompletedTask;
}
