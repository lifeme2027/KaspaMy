// KaspaMy (MIT). Copyright (c) 2026 jun chen.
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace KaspaMy.Mcp;

[McpServerToolType]
public static class McpTools
{
    private const string NetDesc = "Network: testnet-10 (default) or mainnet (mainnet needs KASPAMY_MCP_ALLOW_MAINNET=1 on the server).";
    private const string NodeDesc = "Optional JSON wRPC URL (ws:// or wss://). Default: public node for the network.";

    [McpServerTool(Name = "wallet_create"), Description("Create a new encrypted wallet. The recovery phrase is NEVER returned; back it up with `kaspamy export-mnemonic` in a terminal. Password comes from KASPAMY_PASSWORD.")]
    public static Task<string> WalletCreate([Description(NetDesc)] string? network = null, [Description("Mnemonic length: 12 or 24 (default 24).")] int words = 24)
        => WalletTools.WalletCreate(network, words);

    [McpServerTool(Name = "wallet_import"), Description("Import a BIP39 recovery phrase into an encrypted wallet (m/44'/111111'/0'/0/0). The phrase is never echoed back. Password comes from KASPAMY_PASSWORD.")]
    public static Task<string> WalletImport([Description("BIP39 recovery phrase (12-24 words).")] string mnemonic, [Description(NetDesc)] string? network = null)
        => WalletTools.WalletImport(mnemonic, network);

    [McpServerTool(Name = "wallet_status", ReadOnly = true), Description("Whether a wallet exists, its address, keystore path and the server's safety limits.")]
    public static Task<string> WalletStatus([Description(NetDesc)] string? network = null) => WalletTools.WalletStatus(network);

    [McpServerTool(Name = "get_address", ReadOnly = true), Description("Receive address of the wallet.")]
    public static Task<string> GetAddress([Description(NetDesc)] string? network = null) => WalletTools.GetAddress(network);

    [McpServerTool(Name = "get_balance", ReadOnly = true), Description("Balance and UTXO count from a Kaspa node.")]
    public static Task<string> GetBalance([Description(NetDesc)] string? network = null, [Description(NodeDesc)] string? node = null)
        => WalletTools.GetBalance(network, node);

    [McpServerTool(Name = "estimate_fee", ReadOnly = true), Description("Node feerates and a fee estimate. With to+amount_kas (and KASPAMY_PASSWORD set) builds the exact transaction without broadcasting it.")]
    public static Task<string> EstimateFee([Description(NetDesc)] string? network = null, [Description("Optional recipient address.")] string? to = null,
        [Description("Optional amount in KAS, e.g. \"1.5\".")] string? amount_kas = null, [Description(NodeDesc)] string? node = null)
        => WalletTools.EstimateFee(network, to, amount_kas, node);

    [McpServerTool(Name = "send_kas", Destructive = true), Description("Send KAS. Testnet-10 by default; mainnet disabled unless KASPAMY_MCP_ALLOW_MAINNET=1. Per-transaction cap (default 10 KAS testnet / 1 KAS mainnet). Requires confirm=true after explicit user approval. Password from KASPAMY_PASSWORD only.")]
    public static Task<string> SendKas([Description("Recipient address.")] string to, [Description("Amount in KAS, e.g. \"1.5\".")] string amount_kas,
        [Description("Must be true; set only after the user approved recipient, amount and network.")] bool confirm = false,
        [Description(NetDesc)] string? network = null, [Description(NodeDesc)] string? node = null)
        => WalletTools.SendKas(to, amount_kas, network, confirm, node);

    [McpServerTool(Name = "validate_address", ReadOnly = true), Description("Validate a Kaspa address and report its network and type.")]
    public static Task<string> ValidateAddress([Description("Address to check.")] string address, [Description("Optional expected network.")] string? network = null)
        => WalletTools.ValidateAddress(address, network);
}
