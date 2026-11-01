// KaspaMy (MIT). Copyright (c) 2026 jun chen.
using System.Text.Json;
using System.Text.Json.Serialization;
using KaspaMy.Core;
using KaspaMy.Core.Keystore;
using KaspaMy.Core.Transfer;

namespace KaspaMy.Cli;

/// <summary>
/// Small hand-rolled parser (no System.CommandLine beta dependency). Every command accepts --json.
/// JSON output shape: {"ok":true,"command":"...","data":{...}} or {"ok":false,"command":"...","error":"..."}.
/// </summary>
public static class CliApp
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>For tests: Argon2 parameters (defaults to production parameters).</summary>
    public static KdfParameters? KdfOverride { get; set; }

    public const string ExportConfirmPhrase = "I UNDERSTAND";

    private static readonly HashSet<string> Flags = new() { "--json", "--yes", "-y", "--help", "-h", "--verbose", "--send-max" };

    public static async Task<int> RunAsync(string[] args, IConsoleIO io, CancellationToken ct = default)
    {
        var (cmd, opts, pos) = Parse(args);
        var json = opts.ContainsKey("--json");
        WalletLog.Verbose = opts.ContainsKey("--verbose");
        if (cmd is null || cmd is "help" || opts.ContainsKey("--help") || opts.ContainsKey("-h")) { io.Out(Usage); return cmd is null ? 1 : 0; }
        try
        {
            var netGiven = opts.TryGetValue("--network", out var netText);
            var net = netGiven ? WalletNetworkExtensions.Parse(netText) : WalletNetworkExtensions.Default;
            opts.TryGetValue("--node", out var node);
            object data = cmd switch
            {
                "create" => Create(io, net, opts),
                "import" => Import(io, net),
                "address" => WalletService.Status(net),
                "status" => WalletService.Status(net),
                "balance" => await Balance(net, node, ct),
                "fee" or "estimate-fee" => await Fee(net, node, ct),
                "validate" or "validate-address" => AddressValidator.Validate(Arg(pos, 0, "address"), netGiven ? net : null),
                "send" => await Send(io, net, netGiven, node, opts, pos, json, ct),
                "export-mnemonic" => ExportMnemonic(io, net, json),
                _ => throw new UsageException($"Unknown command '{cmd}'. Run 'kaspamy help'."),
            };
            if (data is Cancelled c) { Emit(io, json, cmd, false, null, c.Reason); return 2; }
            Emit(io, json, cmd, true, data, null);
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var msg = ex switch
            {
                WrongPasswordException => "Wrong password.",
                _ => SecretRedactor.Redact(ex.Message),
            };
            Emit(io, json, cmd, false, null, msg);
            return ex is UsageException ? 64 : 1;
        }
    }

    private sealed record Cancelled(string Reason);
    private sealed class UsageException : Exception { public UsageException(string m) : base(m) { } }

    private static (string? cmd, Dictionary<string, string> opts, List<string> pos) Parse(string[] args)
    {
        var opts = new Dictionary<string, string>(StringComparer.Ordinal);
        var pos = new List<string>();
        string? cmd = null;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("-", StringComparison.Ordinal) && a.Length > 1 && !char.IsDigit(a[1]))
            {
                var eq = a.IndexOf('=');
                if (eq > 0) { opts[a[..eq]] = a[(eq + 1)..]; continue; }
                if (Flags.Contains(a)) { opts[a] = "true"; continue; }
                if (i + 1 >= args.Length) throw new ArgumentException($"Option {a} needs a value.");
                opts[a] = args[++i];
            }
            else if (cmd is null) cmd = a;
            else pos.Add(a);
        }
        return (cmd, opts, pos);
    }

    private static string Arg(List<string> pos, int i, string name) =>
        pos.Count > i ? pos[i] : throw new UsageException($"Missing <{name}>.");

    private static void Emit(IConsoleIO io, bool json, string? cmd, bool ok, object? data, string? error)
    {
        if (json)
        {
            var env = new Dictionary<string, object?> { ["ok"] = ok, ["command"] = cmd };
            if (ok) env["data"] = data; else env["error"] = error;
            io.Out(JsonSerializer.Serialize(env, Json));
            return;
        }
        if (!ok) { io.Err("Error: " + error); return; }
        switch (data)
        {
            case string s: io.Out(s); break;
            case WalletInfo w: io.Out($"Network:  {w.Network}\nAddress:  {w.Address}\nPath:     {w.DerivationPath}\nKeystore: {w.KeystorePath}"); break;
            default:
                foreach (var p in JsonSerializer.SerializeToElement(data, data!.GetType(), Json).EnumerateObject())
                    io.Out($"{p.Name}: {(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText())}");
                break;
        }
    }

    private static string Password(IConsoleIO io, bool confirm)
    {
        var env = io.GetEnv(WalletService.PasswordEnv);
        if (!string.IsNullOrEmpty(env)) return env; // less secure: visible to other processes of the same user, shell history
        if (!io.IsInteractive) throw new InvalidOperationException($"No password: set {WalletService.PasswordEnv} or run interactively.");
        var p = io.ReadSecret("Wallet password: ") ?? "";
        if (confirm)
        {
            WalletService.ValidatePassword(p);
            if (io.ReadSecret("Repeat password: ") != p) throw new ArgumentException("Passwords do not match.");
        }
        return p;
    }

    private static object Create(IConsoleIO io, WalletNetwork net, Dictionary<string, string> opts)
    {
        var words = opts.TryGetValue("--words", out var w) ? int.Parse(w, System.Globalization.CultureInfo.InvariantCulture) : 24;
        if (WalletService.Exists(net)) throw new WalletExistsException(KaspaMyPaths.WalletPath(net));
        var info = new WalletService(KdfOverride).Create(net, Password(io, confirm: true), words);
        return new CreateResult(info, "Wallet created. The recovery phrase is stored ONLY inside the encrypted keystore. Back it up now with: kaspamy export-mnemonic --network " + info.Network);
    }

    public sealed record CreateResult(WalletInfo Wallet, string Next);

    private static object Import(IConsoleIO io, WalletNetwork net)
    {
        if (WalletService.Exists(net)) throw new WalletExistsException(KaspaMyPaths.WalletPath(net));
        var m = io.GetEnv("KASPAMY_MNEMONIC");
        if (string.IsNullOrWhiteSpace(m)) m = io.ReadSecret("Recovery phrase (12-24 words, input hidden): ");
        if (string.IsNullOrWhiteSpace(m)) throw new ArgumentException("No recovery phrase given.");
        return new WalletService(KdfOverride).Import(net, m, Password(io, confirm: true));
    }

    public sealed record BalanceResult(string Network, string Address, string Node, ulong BalanceSompi, string BalanceKas, int UtxoCount, bool NodeSynced, ulong VirtualDaaScore);

    private static async Task<object> Balance(WalletNetwork net, string? node, CancellationToken ct)
    {
        var w = WalletService.Status(net);
        var (n, url, st) = await NodeEndpoints.ConnectAsync(net, node, ct: ct);
        await using var _ = n;
        var coins = await n.GetCoinsAsync(w.Address, ct);
        ulong sum = 0; foreach (var c in coins) sum += c.Amount.Sompi;
        return new BalanceResult(w.Network, w.Address, url, sum, Kas.Format(sum), coins.Count, st.IsSynced, st.VirtualDaaScore);
    }

    private static async Task<object> Fee(WalletNetwork net, string? node, CancellationToken ct)
    {
        var (n, _, _) = await NodeEndpoints.ConnectAsync(net, node, ct: ct);
        await using var __ = n;
        return await SendService.EstimateAsync(n, net, ct);
    }

    public sealed record SendResult(string Network, string From, string To, string AmountKas, string FeeKas, string TransactionId, bool Broadcast, IReadOnlyList<string> Warnings);

    private static async Task<object> Send(IConsoleIO io, WalletNetwork net, bool netGiven, string? node, Dictionary<string, string> opts, List<string> pos, bool json, CancellationToken ct)
    {
        var to = Arg(pos, 0, "to-address");
        var amount = Kas.ParseKas(Arg(pos, 1, "amount-kas"));
        if (net == WalletNetwork.Mainnet && !netGiven) throw new UsageException("Mainnet sends require an explicit --network mainnet.");
        var v = AddressValidator.Validate(to, net);
        if (!v.Valid || !v.MatchesExpectedNetwork) throw new ArgumentException(v.Error ?? "Invalid recipient.");
        var yes = opts.ContainsKey("--yes") || opts.ContainsKey("-y");
        if (!yes && !io.IsInteractive) throw new UsageException("send needs interactive confirmation or --yes.");
        using var acc = WalletService.Unlock(net, Password(io, confirm: false));
        var (n, _, _) = await NodeEndpoints.ConnectAsync(net, node, ct: ct);
        await using var __ = n;
        var plan = await SendService.PrepareAsync(n, acc, to, amount, ct);
        io.Err(plan.Summary());
        if (!yes)
        {
            var expect = net == WalletNetwork.Mainnet ? "SEND MAINNET" : "yes";
            var answer = io.ReadLine($"\nType '{expect}' to broadcast: ");
            if (!string.Equals(answer?.Trim(), expect, StringComparison.Ordinal)) return new Cancelled("Cancelled by user; nothing was broadcast.");
        }
        var id = await TransferService.BroadcastAsync(n, plan, userConfirmed: true, ct, warningsAcknowledged: true);
        return new SendResult(net.Id(), plan.From, plan.To, Kas.Format(plan.AmountSompi), Kas.Format(plan.FeeSompi), id, true, plan.Warnings);
    }

    private static object ExportMnemonic(IConsoleIO io, WalletNetwork net, bool json)
    {
        WalletService.Status(net); // fail early if missing
        if (!io.IsInteractive) throw new UsageException("export-mnemonic requires an interactive terminal (typed confirmation).");
        io.Err("WARNING: the recovery phrase gives full control of this wallet's funds. Anyone who sees it can steal them.\n" +
               "Make sure nobody is watching and that this terminal is not being recorded or logged.");
        var answer = io.ReadLine($"Type '{ExportConfirmPhrase}' to show the recovery phrase: ");
        if (!string.Equals(answer?.Trim(), ExportConfirmPhrase, StringComparison.Ordinal)) return new Cancelled("Cancelled; the recovery phrase was not shown.");
        var pw = io.GetEnv(WalletService.PasswordEnv);
        if (string.IsNullOrEmpty(pw)) pw = io.ReadSecret("Wallet password: ") ?? "";
        var secret = WalletService.ExportMnemonic(net, pw);
        // The single intentional exception to the "never output the mnemonic" rule.
        return new ExportResult(net.Id(), secret.Reveal(), "Write these words down offline. Never paste them into chats, AI tools or websites.");
    }

    public sealed record ExportResult(string Network, string Mnemonic, string Warning);

    public const string Usage = """
        kaspamy - Kaspa command-line wallet (alpha, unaudited; default network testnet-10)

        Usage: kaspamy <command> [options]

        Commands:
          create                      Create a wallet (BIP39, m/44'/111111'/0'/0/0). Does not print the phrase.
          import                      Import a recovery phrase (hidden prompt or KASPAMY_MNEMONIC).
          address                     Show the receive address.
          balance                     Query balance and UTXO count from a node.
          fee                         Show node feerates and a typical transfer fee.
          validate <address>          Validate a Kaspa address.
          send <to> <amount-kas>      Build, sign, confirm and broadcast a transfer.
          export-mnemonic             Show the recovery phrase (interactive, typed confirmation).

        Options:
          --network testnet-10|mainnet  Default testnet-10. Mainnet sends require it explicitly.
          --node ws(s)://...            JSON wRPC endpoint (or KASPAMY_NODE).
          --json                        Machine-readable output.
          --yes                         Skip the send confirmation prompt.
          --words 12|24                 Mnemonic length for create (default 24).
          --verbose                     Info logs on stderr.

        Environment:
          KASPAMY_PASSWORD   Wallet password (less secure than the prompt).
          KASPAMY_HOME       Wallet directory (default %APPDATA%/KaspaMy).
        """;
}
