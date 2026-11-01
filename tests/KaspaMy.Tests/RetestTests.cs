// KaspaMy (MIT). Copyright (c) 2026 jun chen.
// Regression tests for retest findings N1–N6 (testcases/kaspamy-3447f53-retest.md).
using System.Text.Json;
using KaspaMy.Cli;
using KaspaMy.Core;
using KaspaMy.Core.Keystore;
using KaspaMy.Mcp;
using Xunit;

namespace KaspaMy.Tests;

public class RetestTests
{
    private const WalletNetwork Tn = WalletNetwork.Testnet10;
    private static JsonElement P(string s) => JsonDocument.Parse(s).RootElement;

    // ---------- N1 ----------
    [Theory]
    [InlineData("frob", "--send-max")]
    [InlineData("balance", "--node")]
    [InlineData("create", "--words")]
    [InlineData("send", "--network")]
    public async Task N1_option_without_value_is_a_clean_usage_error(string cmd, string opt)
    {
        var io = new FakeIO();
        Assert.Equal(64, await CliApp.RunAsync(new[] { cmd, "--json", opt }, io));
        var j = P(io.Stdout.Single());
        Assert.False(j.GetProperty("ok").GetBoolean());
        Assert.Equal($"Option {opt} needs a value. Run 'kaspamy help'.", j.GetProperty("error").GetString());

        io = new FakeIO();
        Assert.Equal(64, await CliApp.RunAsync(new[] { cmd, opt }, io));
        Assert.DoesNotContain("   at ", io.All);
        Assert.Contains("needs a value", io.All);
    }

    [Fact]
    public async Task N1_option_followed_by_another_option_is_missing_its_value()
    {
        var io = new FakeIO();
        Assert.Equal(64, await CliApp.RunAsync(new[] { "balance", "--node", "--json" }, io));
        Assert.Contains("--node needs a value", io.AllOut);
    }

    // ---------- N2 ----------
    [Fact]
    public void N2_ledger_uses_utc_day_and_owner_only_permissions()
    {
        using var h = new TempHome();
        var old = SpendLedger.UtcNow;
        try
        {
            SpendLedger.UtcNow = () => new DateTime(2026, 11, 1, 23, 30, 0, DateTimeKind.Utc); // 07:30 Nov 2 in UTC+8
            SpendLedger.Reserve(Tn, 10, 100);
            Assert.Equal("2026-11-01", P(File.ReadAllText(SpendLedger.PathFor)).GetProperty("Day").GetString());
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(SpendLedger.PathFor));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(SpendLedger.AnchorPath));
            }
            SpendLedger.UtcNow = () => new DateTime(2026, 11, 2, 0, 1, 0, DateTimeKind.Utc); // next UTC day resets
            Assert.Equal(0UL, SpendLedger.SpentToday(Tn));
        }
        finally { SpendLedger.UtcNow = old; }
    }

    [Fact]
    public void N2_first_use_without_ledger_is_allowed()
    {
        using var h = new TempHome();
        Assert.Equal(0UL, SpendLedger.SpentToday(Tn));
        Assert.Equal(5UL, SpendLedger.Reserve(Tn, 5, 100));
        Assert.True(File.Exists(SpendLedger.AnchorPath));
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Day\":\"2026-11-01\",\"Sompi\":null}")]
    [InlineData("{\"Version\":2,\"Day\":\"2999-01-01\",\"Sompi\":{}}")]
    [InlineData("{\"Version\":2,\"Day\":\"yesterday\",\"Sompi\":{}}")]
    public void N2_missing_null_or_bogus_ledger_fails_closed(string content)
    {
        using var h = new TempHome();
        SpendLedger.Reserve(Tn, 90, 100);
        if (content == "delete") File.Delete(SpendLedger.PathFor); else File.WriteAllText(SpendLedger.PathFor, content);
        var ex = Assert.Throws<UserException>(() => SpendLedger.Reserve(Tn, 1, 100));
        Assert.Contains("fail-closed", ex.Message);
        Assert.Throws<UserException>(() => SpendLedger.SpentToday(Tn));
    }

    [Fact]
    public async Task N2_status_reports_blocked_ledger_instead_of_zero()
    {
        using var h = new TempHome();
        SpendLedger.Reserve(Tn, 90, 100);
        File.WriteAllText(SpendLedger.PathFor, "null");
        var st = P(await WalletTools.WalletStatus(null)).GetProperty("data");
        Assert.StartsWith("unknown (sends blocked)", st.GetProperty("sentTodayKas").GetString());
    }

    [Fact]
    public void N2_reserve_cannot_overflow()
    {
        using var h = new TempHome();
        SpendLedger.Reserve(Tn, 100, ulong.MaxValue);
        Assert.Throws<UserException>(() => SpendLedger.Reserve(Tn, ulong.MaxValue, ulong.MaxValue));
    }

    // ---------- N3 / R2 ----------
    [Theory]
    [InlineData("999999999999", 1_000_000)]
    [InlineData("79228162514264337593543950335", 1_000_000)]
    [InlineData(" 2.5 ", 2.5)]
    [InlineData("", 10)]
    public async Task N3_limit_env_is_clamped(string value, double expectKas)
    {
        using var h = new TempHome();
        h.Set(WalletTools.MaxTestnetEnv, value);
        h.Set(WalletTools.DailyTestnetEnv, value);
        Assert.Equal((ulong)((decimal)expectKas * 100_000_000m), WalletTools.MaxSompi(Tn));
        Assert.Empty(WalletTools.ValidateLimitEnv());
        var st = P(await WalletTools.WalletStatus(null));
        Assert.True(st.GetProperty("ok").GetBoolean());
        Assert.DoesNotContain("Overflow", await WalletTools.SendKas(Vectors.Testnet, "1", null, true, null));
    }

    [Theory]
    [InlineData("1e30")] [InlineData("1e3")] [InlineData("-5")] [InlineData("-0.1")] [InlineData("abc")] [InlineData("5,5")] [InlineData("NaN")]
    public async Task R2_invalid_or_negative_limit_is_a_config_error_not_the_default(string value)
    {
        foreach (var env in WalletTools.LimitEnvs)
        {
            using var h = new TempHome();
            h.Set(env, value);
            var errs = WalletTools.ValidateLimitEnv();
            Assert.Single(errs);
            Assert.Contains(env, errs[0]);
            // even if it slipped past startup, tools fail closed instead of using the default
            h.Set("KASPAMY_PASSWORD", Vectors.Password);
            h.Set(WalletTools.AllowMainnetEnv, "1");
            var net = env.EndsWith("MAINNET") ? "mainnet" : null;
            var r = P(await WalletTools.SendKas(net == null ? Vectors.Testnet : Vectors.Mainnet, "0.1", net, true, null));
            Assert.False(r.GetProperty("ok").GetBoolean());
            Assert.Contains("is invalid", r.GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task R2_zero_limit_forbids_sending_on_that_network_only()
    {
        foreach (var env in WalletTools.LimitEnvs)
        {
            using var h = new TempHome();
            h.Set(env, "0");
            h.Set("KASPAMY_PASSWORD", Vectors.Password);
            h.Set(WalletTools.AllowMainnetEnv, "1");
            Assert.Empty(WalletTools.ValidateLimitEnv());
            var mainnet = env.EndsWith("MAINNET");
            var (addr, net, other, otherNet) = mainnet ? (Vectors.Mainnet, "mainnet", Vectors.Testnet, (string?)null) : (Vectors.Testnet, (string?)null, Vectors.Mainnet, "mainnet");
            var r = P(await WalletTools.SendKas(addr, "0.00000001", net, true, null));
            Assert.Contains("Sending is disabled", r.GetProperty("error").GetString());
            Assert.True(P(await WalletTools.WalletStatus(net)).GetProperty("data").GetProperty("sendingDisabled").GetBoolean());
            Assert.DoesNotContain("Sending is disabled", await WalletTools.SendKas(other, "0.1", otherNet, true, null));
        }
    }

    [Fact]
    public async Task R2_mcp_server_refuses_to_start_with_invalid_limit()
    {
        using var h = new TempHome();
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
        psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "kaspamy-mcp.dll"));
        psi.Environment["KASPAMY_HOME"] = h.Dir;
        psi.Environment[WalletTools.DailyMainnetEnv] = "-1";
        using var p = System.Diagnostics.Process.Start(psi)!;
        var err = await p.StandardError.ReadToEndAsync();
        var outText = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        Assert.Equal(78, p.ExitCode);
        Assert.Contains("configuration error: KASPAMY_MCP_DAILY_MAX_KAS_MAINNET='-1' is invalid", err);
        Assert.Contains("refusing to start", err);
        Assert.Equal("", outText);
    }

    [Fact]
    public void N3_sub_sompi_limit_truncates()
    {
        using var h = new TempHome();
        h.Set(WalletTools.MaxTestnetEnv, "0.000000019");
        Assert.Equal(1UL, WalletTools.MaxSompi(Tn));
    }

    // ---------- N4 ----------
    [Fact]
    public void N4_allowlist_host_case_insensitive_path_exact()
    {
        using var h = new TempHome();
        var def = NodeEndpoints.Testnet10Defaults[0]; // wss://vector-10.kaspa.green/kaspa/testnet-10/wrpc/json
        Assert.Equal(def.Replace("vector-10.kaspa.green", "VECTOR-10.Kaspa.Green").Replace("wss://", "WSS://"),
            WalletTools.CheckNode(Tn, def.Replace("vector-10.kaspa.green", "VECTOR-10.Kaspa.Green").Replace("wss://", "WSS://")));
        Assert.Throws<UserException>(() => WalletTools.CheckNode(Tn, def.Replace("/kaspa/", "/KASPA/")));
        Assert.Throws<UserException>(() => WalletTools.CheckNode(Tn, def.Replace("/json", "/JSON")));
        Assert.Throws<UserException>(() => WalletTools.CheckNode(Tn, def.Replace("kaspa.green", "kaspa.green:443")));
        Assert.Throws<UserException>(() => WalletTools.CheckNode(Tn, def.Replace("wss://", "wss://u@")));
    }

    // ---------- N5 ----------
    [Fact]
    public async Task N5_status_warns_prominently_that_address_is_unverified()
    {
        using var h = new TempHome();
        new WalletService(KdfParameters.TestOnly).Import(Tn, Vectors.Abandon, Vectors.Password);
        var io = new FakeIO();
        Assert.Equal(0, await CliApp.RunAsync(new[] { "status", "--json" }, io));
        var d = P(io.Stdout.Single()).GetProperty("data");
        Assert.False(d.GetProperty("addressVerified").GetBoolean());
        Assert.StartsWith("UNVERIFIED ADDRESS", d.GetProperty("warning").GetString());
        Assert.False(d.TryGetProperty("address", out _)); // only under "unverifiedAddress"
        Assert.Equal(Vectors.Testnet, d.GetProperty("unverifiedAddress").GetString());

        io = new FakeIO();
        Assert.Equal(0, await CliApp.RunAsync(new[] { "status" }, io));
        Assert.Contains(io.Stderr, s => s.StartsWith("!!! UNVERIFIED ADDRESS"));
        Assert.Contains("UNVERIFIED, do not use to receive funds", io.AllOut);
    }

    // ---------- N6 ----------
    [Fact]
    public void N6_stale_temp_files_are_cleaned_and_fresh_ones_kept()
    {
        using var h = new TempHome();
        var path = KaspaMyPaths.WalletPath(Tn);
        Directory.CreateDirectory(h.Dir);
        var stale = path + ".1.dead.tmp"; var fresh = path + ".2.live.tmp"; var other = Path.Combine(h.Dir, "notes.tmp");
        foreach (var f in new[] { stale, fresh, other }) File.WriteAllText(f, "x");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddHours(-1));
        new WalletService(KdfParameters.TestOnly).Import(Tn, Vectors.Abandon, Vectors.Password);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(other));
    }
}
