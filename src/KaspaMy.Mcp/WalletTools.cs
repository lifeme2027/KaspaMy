// KaspaMy (MIT). Copyright (c) 2026 jun chen.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using KaspaMy.Core;
using KaspaMy.Core.Keystore;
using KaspaMy.Core.Transfer;

namespace KaspaMy.Mcp;

/// <summary>
/// Tool logic, independent of the MCP SDK so it can be unit-tested. Every result is a JSON string built from
/// secret-free records and passed through <see cref="SecretRedactor"/> as a last line of defence.
/// Secrets policy: no tool ever returns a mnemonic or private key; the password is read from KASPAMY_PASSWORD only.
/// </summary>
public static class WalletTools
{
    public const string AllowMainnetEnv = "KASPAMY_MCP_ALLOW_MAINNET";
    public const string AllowImportEnv = "KASPAMY_MCP_ALLOW_IMPORT";
    public const string NodesEnv = "KASPAMY_MCP_NODES";
    public const string MaxTestnetEnv = "KASPAMY_MCP_MAX_KAS_TESTNET";
    public const string MaxMainnetEnv = "KASPAMY_MCP_MAX_KAS_MAINNET";
    public const string DailyTestnetEnv = "KASPAMY_MCP_DAILY_MAX_KAS_TESTNET";
    public const string DailyMainnetEnv = "KASPAMY_MCP_DAILY_MAX_KAS_MAINNET";
    public const decimal DefaultMaxTestnetKas = 10m, DefaultMaxMainnetKas = 1m;
    public const decimal DefaultDailyTestnetKas = 50m, DefaultDailyMainnetKas = 2m;

    /// <summary>F1: one wallet-mutating tool (create / import / send) at a time inside this server process.
    /// Cross-process safety comes from <see cref="WalletLock"/> and the atomic no-overwrite create in Core.</summary>
    private static readonly SemaphoreSlim Mutex = new(1, 1);

    /// <summary>Tests only.</summary>
    internal static KdfParameters? KdfOverride { get; set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool MainnetAllowed => Environment.GetEnvironmentVariable(AllowMainnetEnv) == "1";
    public static bool ImportAllowed => Environment.GetEnvironmentVariable(AllowImportEnv) == "1";

    /// <summary>N3: upper bound for any configured limit (well below ulong overflow; above the KAS max supply is meaningless).</summary>
    public const decimal LimitCeilingKas = 1_000_000m;

    /// <summary>N3: parses a KAS limit env var. Invalid, zero or negative -> default; above the ceiling -> clamped; sub-sompi digits truncated.
    /// Never throws, so a bad value cannot turn into an overflow or an unexpected error.</summary>
    internal static ulong KasEnv(string env, decimal def)
    {
        var v = Environment.GetEnvironmentVariable(env);
        decimal kas = def;
        if (!string.IsNullOrWhiteSpace(v))
        {
            if (decimal.TryParse(v.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) && d > 0)
            {
                kas = d;
                if (kas > LimitCeilingKas) { WalletLog.Warn($"{env}={v} is above the ceiling; using {LimitCeilingKas} KAS."); kas = LimitCeilingKas; }
            }
            else WalletLog.Warn($"{env} is not a positive decimal; using the default {def} KAS.");
        }
        return (ulong)decimal.Truncate(kas * Kas.SompiPerKas);
    }

    /// <summary>Per-transaction cap on amount + fee (F6).</summary>
    public static ulong MaxSompi(WalletNetwork n) => n == WalletNetwork.Mainnet
        ? KasEnv(MaxMainnetEnv, DefaultMaxMainnetKas) : KasEnv(MaxTestnetEnv, DefaultMaxTestnetKas);

    /// <summary>Per-day cap on amount + fee across all sends (F3).</summary>
    public static ulong DailyMaxSompi(WalletNetwork n) => n == WalletNetwork.Mainnet
        ? KasEnv(DailyMainnetEnv, DefaultDailyMainnetKas) : KasEnv(DailyTestnetEnv, DefaultDailyTestnetKas);

    /// <summary>F4: nodes the agent may pick: the built-in defaults, KASPAMY_NODE, and KASPAMY_MCP_NODES (comma-separated).</summary>
    public static IReadOnlyList<string> AllowedNodes(WalletNetwork n)
    {
        var list = new List<string>();
        if (n == WalletNetwork.Mainnet) list.Add(NodeEndpoints.MainnetDefault); else list.AddRange(NodeEndpoints.Testnet10Defaults);
        var env = Environment.GetEnvironmentVariable(NodeEndpoints.NodeEnv);
        if (!string.IsNullOrWhiteSpace(env)) list.Add(env.Trim());
        foreach (var x in (Environment.GetEnvironmentVariable(NodesEnv) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            list.Add(x);
        return list;
    }

    internal static string? CheckNode(WalletNetwork n, string? node)
    {
        if (string.IsNullOrWhiteSpace(node)) return null; // server-side default (KASPAMY_NODE or built-ins)
        var t = node.Trim();
        if (!AllowedNodes(n).Any(a => NodeEquals(a, t)))
            throw new UserException($"Node '{t}' is not on the server's allowlist. Allowed: {string.Join(", ", AllowedNodes(n))}. " +
                $"The operator can add nodes with {NodesEnv} (comma-separated) or set {NodeEndpoints.NodeEnv}.");
        return t;
    }

    /// <summary>N4: scheme and host compare case-insensitively (DNS semantics); port, path, query compare exactly.</summary>
    internal static bool NodeEquals(string allowed, string candidate)
    {
        static (string head, string rest) Split(string u)
        {
            var s = u.IndexOf("://", StringComparison.Ordinal);
            if (s < 0) return (u, "");
            var p = u.IndexOf('/', s + 3);
            return p < 0 ? (u, "") : (u[..p], u[p..]);
        }
        var (ah, ar) = Split(allowed); var (ch, cr) = Split(candidate);
        // head = scheme://authority (no userinfo is ever in the allowlist, and '@' would not match) -> case-insensitive
        return string.Equals(ah, ch, StringComparison.OrdinalIgnoreCase) && string.Equals(ar, cr, StringComparison.Ordinal);
    }

    private static string Ok(object data) => SecretRedactor.Redact(JsonSerializer.Serialize(new { ok = true, data }, Json));
    internal static string Fail(string error) => SecretRedactor.Redact(JsonSerializer.Serialize(new { ok = false, error = SecretRedactor.Redact(error) }, Json));

    private static WalletNetwork ParseNet(string? network) =>
        string.IsNullOrWhiteSpace(network) ? WalletNetworkExtensions.Default : WalletNetworkExtensions.Parse(network);

    /// <summary>Parses the network argument; mainnet is refused unless KASPAMY_MCP_ALLOW_MAINNET=1.</summary>
    private static WalletNetwork Net(string? network)
    {
        var n = ParseNet(network);
        if (n == WalletNetwork.Mainnet && !MainnetAllowed)
            throw new UserException($"Mainnet is disabled in the MCP server. Set {AllowMainnetEnv}=1 in the server environment to enable it.");
        return n;
    }

    private static string Password()
    {
        var p = Environment.GetEnvironmentVariable(WalletService.PasswordEnv);
        if (string.IsNullOrEmpty(p)) throw new UserException($"{WalletService.PasswordEnv} is not set in the MCP server environment. Passwords are never accepted as tool arguments.");
        return p;
    }

    private static async Task<string> Guard(string tool, Func<Task<string>> f, bool exclusive = false)
    {
        if (exclusive) await Mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            WalletLog.Info($"tool {tool}");
            return await f().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var msg = ex is WrongPasswordException w ? w.Message.TrimEnd('.') + " (check KASPAMY_PASSWORD)." : ErrorText.Safe(ex); // F7
            WalletLog.Warn($"tool {tool} failed: {ex.GetType().Name}: {msg}");
            return Fail(msg);
        }
        finally
        {
            if (exclusive) Mutex.Release();
        }
    }

    public static Task<string> WalletCreate(string? network, int words = 24) => Guard("wallet_create", () =>
    {
        var n = Net(network);
        if (words is not (12 or 24)) throw new UserException("words must be 12 or 24.");
        var info = new WalletService(KdfOverride).Create(n, Password(), words);
        return Task.FromResult(Ok(new
        {
            wallet = info,
            mnemonicReturned = false,
            backup = $"The recovery phrase is stored only in the encrypted keystore and is never returned by this server. " +
                     $"Back it up now from a terminal: kaspamy export-mnemonic --network {info.Network}",
        }));
    }, exclusive: true);

    /// <summary>F5: disabled unless KASPAMY_MCP_ALLOW_IMPORT=1, because the phrase passes through the model's context.</summary>
    public static Task<string> WalletImport(string mnemonic, string? network) => Guard("wallet_import", () =>
    {
        if (!ImportAllowed)
            throw new UserException($"wallet_import is disabled: a recovery phrase sent through an AI chat is exposed to the model, its provider and chat logs. " +
                $"Import from a terminal instead (kaspamy import), or set {AllowImportEnv}=1 on the server to accept the risk.");
        var n = Net(network);
        var info = new WalletService(KdfOverride).Import(n, mnemonic, Password());
        return Task.FromResult(Ok(new { wallet = info }));
    }, exclusive: true);

    public static Task<string> WalletStatus(string? network) => Guard("wallet_status", () =>
    {
        var n = ParseNet(network);
        var exists = WalletService.Exists(n);
        var pw = Environment.GetEnvironmentVariable(WalletService.PasswordEnv);
        WalletInfo? verified = null; string? note = null;
        if (exists && !string.IsNullOrEmpty(pw))
        {
            try { verified = WalletService.Info(n, pw); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { note = "Address not verified: " + ErrorText.Safe(ex); }
        }
        else if (exists) note = $"Address hidden: set {WalletService.PasswordEnv} so the server can verify it against the encrypted keystore.";
        return Task.FromResult(Ok(new
        {
            network = n.Id(),
            exists,
            wallet = verified, // F2: only a decrypted-and-verified address is ever returned
            addressVerified = verified is not null,
            note,
            keystorePath = KaspaMyPaths.WalletPath(n),
            passwordConfigured = !string.IsNullOrEmpty(pw),
            mainnetAllowed = MainnetAllowed,
            importAllowed = ImportAllowed,
            maxSendKas = Kas.Format(MaxSompi(n)),
            dailyMaxKas = Kas.Format(DailyMaxSompi(n)),
            sentTodayKas = SentTodayText(n),
        }));
    });

    private static string SentTodayText(WalletNetwork n)
    {
        try { return Kas.Format(SpendLedger.SpentToday(n)); }
        catch (UserException ex) { return "unknown (sends blocked): " + ex.Message; }
    }

    public static Task<string> GetAddress(string? network) => Guard("get_address", () =>
    {
        var w = WalletService.Info(Net(network), Password()); // F2
        return Task.FromResult(Ok(new { w.Network, w.Address, w.DerivationPath, w.AddressVerified }));
    });

    public static Task<string> GetBalance(string? network, string? node) => Guard("get_balance", async () =>
    {
        var n = Net(network);
        var nodeUrl = CheckNode(n, node);
        var w = WalletService.Info(n, Password()); // F2
        var (rpc, url, st) = await NodeEndpoints.ConnectAsync(n, nodeUrl).ConfigureAwait(false);
        await using var _ = rpc;
        var coins = await rpc.GetCoinsAsync(w.Address).ConfigureAwait(false);
        ulong sum = 0; foreach (var c in coins) sum += c.Amount.Sompi;
        return Ok(new { network = n.Id(), w.Address, node = url, balanceSompi = sum, balanceKas = Kas.Format(sum), utxoCount = coins.Count, st.IsSynced, st.VirtualDaaScore });
    });

    public static Task<string> EstimateFee(string? network, string? to, string? amountKas, string? node) => Guard("estimate_fee", async () =>
    {
        var n = Net(network);
        var nodeUrl = CheckNode(n, node);
        var (rpc, url, _) = await NodeEndpoints.ConnectAsync(n, nodeUrl).ConfigureAwait(false);
        await using var __ = rpc;
        var pw = Environment.GetEnvironmentVariable(WalletService.PasswordEnv);
        if (!string.IsNullOrWhiteSpace(to) && !string.IsNullOrWhiteSpace(amountKas) && !string.IsNullOrEmpty(pw) && WalletService.Exists(n))
        {
            using var acc = WalletService.Unlock(n, pw);
            var plan = await SendService.PrepareAsync(rpc, acc, AddressValidator.Normalize(to), Kas.ParseKas(amountKas)).ConfigureAwait(false);
            var rates = await rpc.GetFeeRatesAsync().ConfigureAwait(false);
            return Ok(new { node = url, estimate = SendService.FromPlan(plan, rates), totalKas = Kas.Format(plan.AmountSompi + plan.FeeSompi), warnings = plan.Warnings });
        }
        return Ok(new { node = url, estimate = await SendService.EstimateAsync(rpc, n).ConfigureAwait(false) });
    });

    public static Task<string> ValidateAddress(string address, string? network) => Guard("validate_address", () =>
    {
        WalletNetwork? n = string.IsNullOrWhiteSpace(network) ? null : WalletNetworkExtensions.Parse(network);
        return Task.FromResult(Ok(AddressValidator.Validate(address, n)));
    });

    /// <summary>Pre-flight checks that need no wallet or node. Internal for tests.</summary>
    internal static (WalletNetwork n, string to, ulong amount, string? node) PreCheck(string to, string amountKas, string? network, bool confirm, string? node)
    {
        var n = Net(network);
        if (!confirm) throw new UserException("send_kas requires confirm=true after the user has approved the recipient, amount and network.");
        var amount = Kas.ParseKas(amountKas);
        CheckCap(n, amount, 0);
        var nodeUrl = CheckNode(n, node);
        var addr = AddressValidator.Normalize(to);
        var v = AddressValidator.Validate(addr, n);
        if (!v.Valid || !v.MatchesExpectedNetwork) throw new UserException(v.Error ?? "Invalid recipient.");
        return (n, addr, amount, nodeUrl);
    }

    /// <summary>F6: the per-transaction cap covers amount + fee.</summary>
    internal static void CheckCap(WalletNetwork n, ulong amount, ulong fee)
    {
        var max = MaxSompi(n);
        if (amount + fee > max)
            throw new UserException($"Amount {Kas.Format(amount)} KAS + fee {Kas.Format(fee)} KAS exceeds the per-transaction limit of {Kas.Format(max)} KAS on {n.Id()} " +
                $"(configure with {(n == WalletNetwork.Mainnet ? MaxMainnetEnv : MaxTestnetEnv)}).");
    }

    public static Task<string> SendKas(string to, string amountKas, string? network, bool confirm, string? node) => Guard("send_kas", async () =>
    {
        var (n, addr, amount, nodeUrl) = PreCheck(to, amountKas, network, confirm, node);
        using var acc = WalletService.Unlock(n, Password());
        var (rpc, url, _) = await NodeEndpoints.ConnectAsync(n, nodeUrl).ConfigureAwait(false);
        await using var __ = rpc;
        var plan = await SendService.PrepareAsync(rpc, acc, addr, amount).ConfigureAwait(false);
        CheckCap(n, plan.AmountSompi, plan.FeeSompi);
        if (plan.Warnings.Count > 0)
            return Fail("Not broadcast: the transfer has fee warnings that need a human decision. Use the CLI (kaspamy send) to review: " + string.Join(" | ", plan.Warnings));
        var total = plan.AmountSompi + plan.FeeSompi;
        var spent = SpendLedger.Reserve(n, total, DailyMaxSompi(n)); // F3
        string id;
        try { id = await TransferService.BroadcastAsync(rpc, plan, userConfirmed: true).ConfigureAwait(false); }
        catch (TransferException) { SpendLedger.Release(n, total); throw; } // definitely not sent; status-unknown keeps the reservation
        WalletLog.Info($"send_kas broadcast {id} network={n.Id()} amount={Kas.Format(amount)}");
        return Ok(new
        {
            network = n.Id(), node = url, from = plan.From, to = plan.To, amountKas = Kas.Format(plan.AmountSompi), feeKas = Kas.Format(plan.FeeSompi),
            totalKas = Kas.Format(total), sentTodayKas = Kas.Format(spent), dailyMaxKas = Kas.Format(DailyMaxSompi(n)), transactionId = id,
        });
    }, exclusive: true);
}
