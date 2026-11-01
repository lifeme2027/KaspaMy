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
    public const string MaxTestnetEnv = "KASPAMY_MCP_MAX_KAS_TESTNET";
    public const string MaxMainnetEnv = "KASPAMY_MCP_MAX_KAS_MAINNET";
    public const decimal DefaultMaxTestnetKas = 10m, DefaultMaxMainnetKas = 1m;

    /// <summary>Tests only.</summary>
    internal static KdfParameters? KdfOverride { get; set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool MainnetAllowed => Environment.GetEnvironmentVariable(AllowMainnetEnv) == "1";

    public static ulong MaxSompi(WalletNetwork n)
    {
        var (env, def) = n == WalletNetwork.Mainnet ? (MaxMainnetEnv, DefaultMaxMainnetKas) : (MaxTestnetEnv, DefaultMaxTestnetKas);
        var v = Environment.GetEnvironmentVariable(env);
        var kas = decimal.TryParse(v, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) && d > 0 ? d : def;
        return (ulong)(kas * Kas.SompiPerKas);
    }

    private static string Ok(object data) => SecretRedactor.Redact(JsonSerializer.Serialize(new { ok = true, data }, Json));
    internal static string Fail(string error) => SecretRedactor.Redact(JsonSerializer.Serialize(new { ok = false, error = SecretRedactor.Redact(error) }, Json));

    /// <summary>Parses the network argument; mainnet is refused unless KASPAMY_MCP_ALLOW_MAINNET=1.</summary>
    private static WalletNetwork Net(string? network)
    {
        var n = string.IsNullOrWhiteSpace(network) ? WalletNetworkExtensions.Default : WalletNetworkExtensions.Parse(network);
        if (n == WalletNetwork.Mainnet && !MainnetAllowed)
            throw new InvalidOperationException($"Mainnet is disabled in the MCP server. Set {AllowMainnetEnv}=1 in the server environment to enable it.");
        return n;
    }

    private static string Password()
    {
        var p = Environment.GetEnvironmentVariable(WalletService.PasswordEnv);
        if (string.IsNullOrEmpty(p)) throw new InvalidOperationException($"{WalletService.PasswordEnv} is not set in the MCP server environment. Passwords are never accepted as tool arguments.");
        return p;
    }

    private static async Task<string> Guard(string tool, Func<Task<string>> f)
    {
        try
        {
            WalletLog.Info($"tool {tool}");
            return await f().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var msg = ex is WrongPasswordException ? "Wrong password (check KASPAMY_PASSWORD)." : ex.Message;
            WalletLog.Warn($"tool {tool} failed: {ex.GetType().Name}: {msg}");
            return Fail(msg);
        }
    }

    public static Task<string> WalletCreate(string? network, int words = 24) => Guard("wallet_create", () =>
    {
        var n = Net(network);
        var info = new WalletService(KdfOverride).Create(n, Password(), words == 12 ? 12 : 24);
        return Task.FromResult(Ok(new
        {
            wallet = info,
            mnemonicReturned = false,
            backup = $"The recovery phrase is stored only in the encrypted keystore and is never returned by this server. " +
                     $"Back it up now from a terminal: kaspamy export-mnemonic --network {info.Network}",
        }));
    });

    public static Task<string> WalletImport(string mnemonic, string? network) => Guard("wallet_import", () =>
    {
        var n = Net(network);
        var info = new WalletService(KdfOverride).Import(n, mnemonic, Password());
        return Task.FromResult(Ok(new { wallet = info }));
    });

    public static Task<string> WalletStatus(string? network) => Guard("wallet_status", () =>
    {
        var n = string.IsNullOrWhiteSpace(network) ? WalletNetworkExtensions.Default : WalletNetworkExtensions.Parse(network);
        var exists = WalletService.Exists(n);
        return Task.FromResult(Ok(new
        {
            network = n.Id(),
            exists,
            wallet = exists ? WalletService.Status(n) : null,
            keystorePath = KaspaMyPaths.WalletPath(n),
            passwordConfigured = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(WalletService.PasswordEnv)),
            mainnetAllowed = MainnetAllowed,
            maxSendKas = Kas.Format(MaxSompi(n)),
        }));
    });

    public static Task<string> GetAddress(string? network) => Guard("get_address", () =>
    {
        var w = WalletService.Status(Net(network));
        return Task.FromResult(Ok(new { w.Network, w.Address, w.DerivationPath }));
    });

    public static Task<string> GetBalance(string? network, string? node) => Guard("get_balance", async () =>
    {
        var n = Net(network);
        var w = WalletService.Status(n);
        var (rpc, url, st) = await NodeEndpoints.ConnectAsync(n, node).ConfigureAwait(false);
        await using var _ = rpc;
        var coins = await rpc.GetCoinsAsync(w.Address).ConfigureAwait(false);
        ulong sum = 0; foreach (var c in coins) sum += c.Amount.Sompi;
        return Ok(new { network = n.Id(), w.Address, node = url, balanceSompi = sum, balanceKas = Kas.Format(sum), utxoCount = coins.Count, st.IsSynced, st.VirtualDaaScore });
    });

    public static Task<string> EstimateFee(string? network, string? to, string? amountKas, string? node) => Guard("estimate_fee", async () =>
    {
        var n = Net(network);
        var (rpc, url, _) = await NodeEndpoints.ConnectAsync(n, node).ConfigureAwait(false);
        await using var __ = rpc;
        var pw = Environment.GetEnvironmentVariable(WalletService.PasswordEnv);
        if (!string.IsNullOrWhiteSpace(to) && !string.IsNullOrWhiteSpace(amountKas) && !string.IsNullOrEmpty(pw) && WalletService.Exists(n))
        {
            using var acc = WalletService.Unlock(n, pw);
            var plan = await SendService.PrepareAsync(rpc, acc, to, Kas.ParseKas(amountKas)).ConfigureAwait(false);
            var rates = await rpc.GetFeeRatesAsync().ConfigureAwait(false);
            return Ok(new { node = url, estimate = SendService.FromPlan(plan, rates), warnings = plan.Warnings });
        }
        return Ok(new { node = url, estimate = await SendService.EstimateAsync(rpc, n).ConfigureAwait(false) });
    });

    public static Task<string> ValidateAddress(string address, string? network) => Guard("validate_address", () =>
    {
        WalletNetwork? n = string.IsNullOrWhiteSpace(network) ? null : WalletNetworkExtensions.Parse(network);
        return Task.FromResult(Ok(AddressValidator.Validate(address, n)));
    });

    public static Task<string> SendKas(string to, string amountKas, string? network, bool confirm, string? node) => Guard("send_kas", async () =>
    {
        var n = Net(network);
        if (!confirm) throw new InvalidOperationException("send_kas requires confirm=true after the user has approved the recipient, amount and network.");
        var amount = Kas.ParseKas(amountKas);
        var max = MaxSompi(n);
        if (amount > max)
            throw new InvalidOperationException($"Amount {Kas.Format(amount)} KAS exceeds the per-transaction limit of {Kas.Format(max)} KAS on {n.Id()} " +
                $"(configure with {(n == WalletNetwork.Mainnet ? MaxMainnetEnv : MaxTestnetEnv)}).");
        var v = AddressValidator.Validate(to, n);
        if (!v.Valid || !v.MatchesExpectedNetwork) throw new ArgumentException(v.Error ?? "Invalid recipient.");
        using var acc = WalletService.Unlock(n, Password());
        var (rpc, url, _) = await NodeEndpoints.ConnectAsync(n, node).ConfigureAwait(false);
        await using var __ = rpc;
        var plan = await SendService.PrepareAsync(rpc, acc, to, amount).ConfigureAwait(false);
        if (plan.Warnings.Count > 0)
            return Fail("Not broadcast: the transfer has fee warnings that need a human decision. Use the CLI (kaspamy send) to review: " + string.Join(" | ", plan.Warnings));
        var id = await TransferService.BroadcastAsync(rpc, plan, userConfirmed: true).ConfigureAwait(false);
        WalletLog.Info($"send_kas broadcast {id} network={n.Id()} amount={Kas.Format(amount)}");
        return Ok(new { network = n.Id(), node = url, from = plan.From, to = plan.To, amountKas = Kas.Format(plan.AmountSompi), feeKas = Kas.Format(plan.FeeSompi), transactionId = id });
    });
}
