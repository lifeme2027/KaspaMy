// KaspaMy (MIT). Copyright (c) 2026 jun chen.
using System.Net.Http.Json;
using System.Text.Json;
using KaspaMy.Core.Rpc;

namespace KaspaMy.Core;

/// <summary>
/// Picks the wRPC JSON endpoint. Order: explicit --node / KASPAMY_NODE → built-in defaults (mainnet: the NKaspa sample default
/// wss://eva.kaspa.green/kaspa/mainnet/wrpc/json; testnet-10: public PNN nodes) → on failure, Kaspa PNN resolver (GET {resolver}/v2/kaspa/{net}/tls/wrpc/json).
/// NKaspa 0.1.10-alpha ships no resolver, so this minimal one lives here (see docs/NKASPA-REQUESTS.md, R1).
/// </summary>
public static class NodeEndpoints
{
    public const string NodeEnv = "KASPAMY_NODE";
    public const string MainnetDefault = "wss://eva.kaspa.green/kaspa/mainnet/wrpc/json";

    /// <summary>Public PNN testnet-10 JSON wRPC nodes (verified reachable 2026-11-01). Neither NKaspa nor KasNetWallet ships one
    /// (KasNetWallet defaults to a local node, ws://127.0.0.1:18210).</summary>
    public static readonly string[] Testnet10Defaults =
    {
        "wss://vector-10.kaspa.green/kaspa/testnet-10/wrpc/json",
        "wss://muon-10.kaspa.blue/kaspa/testnet-10/wrpc/json",
    };

    /// <summary>Public resolver services (rusty-kaspa rpc/wrpc/client/Resolvers.toml).</summary>
    public static readonly string[] Resolvers =
    {
        "https://eric.kaspa.stream", "https://maxim.kaspa.stream", "https://sean.kaspa.stream", "https://troy.kaspa.stream",
        "https://john.kaspa.red", "https://mike.kaspa.red", "https://paul.kaspa.red", "https://alex.kaspa.red",
        "https://jake.kaspa.green", "https://mark.kaspa.green", "https://adam.kaspa.green", "https://liam.kaspa.green",
        "https://noah.kaspa.blue", "https://ryan.kaspa.blue", "https://jack.kaspa.blue", "https://luke.kaspa.blue",
    };

    public static string ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) || (u.Scheme != "ws" && u.Scheme != "wss"))
            throw new ArgumentException("Node URL must be a ws:// or wss:// JSON wRPC endpoint.");
        return u.ToString();
    }

    /// <summary>Candidate URLs in order (explicit override only, if given).</summary>
    public static async Task<IReadOnlyList<string>> CandidatesAsync(WalletNetwork n, string? overrideUrl, CancellationToken ct = default, bool resolve = true)
    {
        overrideUrl ??= Environment.GetEnvironmentVariable(NodeEnv);
        if (!string.IsNullOrWhiteSpace(overrideUrl)) return new[] { ValidateUrl(overrideUrl) };
        var list = new List<string>();
        if (n == WalletNetwork.Mainnet) list.Add(MainnetDefault);
        else list.AddRange(Testnet10Defaults);
        if (!resolve) return list;
        var r = await ResolveAsync(n, ct).ConfigureAwait(false);
        if (r is not null && !list.Contains(r)) list.Add(r);
        return list;
    }

    public static async Task<string?> ResolveAsync(WalletNetwork n, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        foreach (var r in Resolvers.OrderBy(_ => Random.Shared.Next()))
        {
            try
            {
                using var resp = await http.GetAsync($"{r}/v2/kaspa/{n.Id()}/tls/wrpc/json", ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;
                var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
                if (doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("url", out var u) && u.GetString() is string s && s.StartsWith("ws", StringComparison.Ordinal))
                    return s;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException) { }
        }
        return null;
    }

    /// <summary>Connects to the first reachable candidate and checks its network id matches.</summary>
    public static async Task<(IWalletNode Node, string Url, NodeStatus Status)> ConnectAsync(WalletNetwork n, string? overrideUrl, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var cands = await CandidatesAsync(n, overrideUrl, ct, resolve: false).ConfigureAwait(false);
        if (cands.Count == 0) throw new NodeUnavailableException($"No {n.Id()} node found: the public resolver returned none. Pass --node ws(s)://host:port (JSON wRPC; tn10 default port 18210) or set {NodeEnv}.");
        var errors = new List<string>();
        foreach (var url in cands)
        {
            IWalletNode? node = null;
            try
            {
                node = await WrpcWalletNode.ConnectAsync(url, ct, timeout ?? TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var st = await node.GetStatusAsync(ct).ConfigureAwait(false);
                if (!string.Equals(st.NetworkId, n.ExpectedNodeNetworkId(), StringComparison.OrdinalIgnoreCase))
                    throw new NodeUnavailableException($"node reports network '{st.NetworkId}', expected '{n.Id()}'");
                return (node, url, st);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                if (node is not null) await node.DisposeAsync().ConfigureAwait(false);
                errors.Add($"{url}: {(ex is NodeUnavailableException ? ex.Message : ex.GetType().Name)}"); // F7: no inner messages
            }
        }
        if (string.IsNullOrWhiteSpace(overrideUrl ?? Environment.GetEnvironmentVariable(NodeEnv)))
        {
            var r = await ResolveAsync(n, ct).ConfigureAwait(false);
            if (r is not null) return await ConnectAsync(n, r, timeout, ct).ConfigureAwait(false);
        }
        throw new NodeUnavailableException("Could not connect to a node: " + string.Join("; ", errors));
    }
}

public sealed class NodeUnavailableException : Exception
{
    public NodeUnavailableException(string message) : base(message) { }
}
