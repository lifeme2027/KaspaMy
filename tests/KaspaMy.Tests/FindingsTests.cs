// KaspaMy (MIT). Copyright (c) 2026 jun chen.
// Regression tests for acceptance findings F1–F11 (testcases/kaspamy-d4f89d6.md).
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using KaspaMy.Cli;
using KaspaMy.Core;
using KaspaMy.Core.Keystore;
using KaspaMy.Mcp;
using Xunit;

namespace KaspaMy.Tests;

public class FindingsTests
{
    private static JsonElement P(string s) => JsonDocument.Parse(s).RootElement;
    private const WalletNetwork Tn = WalletNetwork.Testnet10;

    private static void EditWallet(Action<JsonObject> edit)
    {
        var path = KaspaMyPaths.WalletPath(Tn);
        var o = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(o);
        File.WriteAllText(path, o.ToJsonString());
    }

    // ---------- F1: concurrent create/import ----------

    [Fact]
    public async Task F1_parallel_creates_in_process_yield_one_wallet_matching_the_reported_address()
    {
        using var h = new TempHome();
        var svc = new WalletService(KdfParameters.TestOnly);
        var tasks = Enumerable.Range(0, 12).Select(i => Task.Run(() =>
        {
            try { return (ok: true, addr: i % 2 == 0 ? svc.Create(Tn, Vectors.Password, 12).Address : svc.Import(Tn, MnemonicService.Generate(12), Vectors.Password).Address, err: (Exception?)null); }
            catch (Exception ex) { return (ok: false, addr: (string?)null, err: ex); }
        })).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, r => r.ok);
        Assert.All(results.Where(r => !r.ok), r => Assert.IsType<WalletExistsException>(r.err));
        Assert.Equal(results.Single(r => r.ok).addr, WalletService.Info(Tn, Vectors.Password).Address);
        Assert.Empty(Directory.GetFiles(h.Dir, "*.tmp"));
    }

    [Fact]
    public void F1_SaveNew_is_atomic_and_never_overwrites()
    {
        using var h = new TempHome();
        var path = KaspaMyPaths.WalletPath(Tn);
        var files = Enumerable.Range(0, 16).Select(_ => WalletKeystore.Encrypt(MnemonicService.Generate(12), Vectors.Password, Tn, KdfParameters.TestOnly)).ToArray();
        var winners = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 16 }, f =>
        {
            try { WalletKeystore.SaveNew(f, path); winners.Add(f.Address); } catch (IOException) { }
        });
        Assert.Single(winners);
        Assert.Equal(winners.Single(), WalletKeystore.Load(path).Address);
        Assert.Empty(Directory.GetFiles(h.Dir, "*.tmp"));
    }

    [Fact]
    public async Task F1_parallel_mcp_wallet_create_calls_are_serialized()
    {
        using var h = new TempHome();
        h.Set("KASPAMY_PASSWORD", Vectors.Password);
        var rs = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => WalletTools.WalletCreate(null, 12))));
        var ok = rs.Select(P).Where(r => r.GetProperty("ok").GetBoolean()).ToList();
        Assert.Single(ok);
        Assert.Equal(ok[0].GetProperty("data").GetProperty("wallet").GetProperty("address").GetString(), WalletService.Info(Tn, Vectors.Password).Address);
        Assert.All(rs.Select(P).Where(r => !r.GetProperty("ok").GetBoolean()), r => Assert.Contains("already exists", r.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task F1_parallel_cli_processes_yield_one_wallet()
    {
        using var h = new TempHome();
        var cli = Path.Combine(AppContext.BaseDirectory, "kaspamy.dll");
        var procs = Enumerable.Range(0, 4).Select(_ =>
        {
            var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
            foreach (var a in new[] { cli, "create", "--words", "12", "--json" }) psi.ArgumentList.Add(a);
            psi.Environment["KASPAMY_HOME"] = h.Dir;
            psi.Environment["KASPAMY_PASSWORD"] = Vectors.Password;
            return Process.Start(psi)!;
        }).ToList();
        var outs = await Task.WhenAll(procs.Select(async p => { var o = await p.StandardOutput.ReadToEndAsync(); await p.WaitForExitAsync(); return o; }));
        var ok = outs.Select(o => P(o.Trim())).Where(j => j.GetProperty("ok").GetBoolean()).ToList();
        Assert.Single(ok);
        Assert.Equal(ok[0].GetProperty("data").GetProperty("wallet").GetProperty("address").GetString(), WalletService.Info(Tn, Vectors.Password).Address);
        Assert.Empty(Directory.GetFiles(h.Dir, "*.tmp"));
    }

    // ---------- F2: address must be verified ----------

    [Fact]
    public async Task F2_swapped_address_is_never_shown_as_receive_address()
    {
        using var h = new TempHome();
        h.Set("KASPAMY_PASSWORD", Vectors.Password);
        new WalletService(KdfParameters.TestOnly).Import(Tn, Vectors.Abandon, Vectors.Password);
        Assert.True(WalletService.Info(Tn, Vectors.Password).AddressVerified);
        const string attacker = "kaspatest:qypqwzn4hawgas3ty6yeg4p9pw5m0j4j3ls7fl6yhyuuy4ny8aakjmc9fy8yt8n";
        EditWallet(o => o["Address"] = attacker);

        var io = new FakeIO(); io.Env["KASPAMY_PASSWORD"] = Vectors.Password;
        var code = await CliApp.RunAsync(new[] { "address", "--json" }, io);
        Assert.NotEqual(0, code);
        Assert.DoesNotContain(attacker, io.All);
        Assert.Contains("modified", io.AllOut);

        var mcp = await WalletTools.GetAddress(null);
        Assert.DoesNotContain(attacker, mcp);
        Assert.False(P(mcp).GetProperty("ok").GetBoolean());
        var st = P(await WalletTools.WalletStatus(null)).GetProperty("data");
        Assert.False(st.GetProperty("addressVerified").GetBoolean());
        Assert.DoesNotContain(attacker, st.ToString());
        Assert.False(WalletService.Status(Tn).AddressVerified); // header read is explicitly unverified
    }

    [Fact]
    public async Task F2_status_without_password_hides_address_in_mcp()
    {
        using var h = new TempHome();
        new WalletService(KdfParameters.TestOnly).Import(Tn, Vectors.Abandon, Vectors.Password);
        var st = P(await WalletTools.WalletStatus(null)).GetProperty("data");
        Assert.True(st.GetProperty("exists").GetBoolean());
        Assert.False(st.GetProperty("addressVerified").GetBoolean());
        Assert.False(st.TryGetProperty("wallet", out _));
    }

    // ---------- F3: daily limit ----------

    [Fact]
    public void F3_daily_limit_accumulates_and_persists()
    {
        using var h = new TempHome();
        var max = 100_000_000UL;
        SpendLedger.Reserve(Tn, 60_000_000, max);
        Assert.Equal(60_000_000UL, SpendLedger.SpentToday(Tn));
        var ex = Assert.Throws<UserException>(() => SpendLedger.Reserve(Tn, 50_000_000, max));
        Assert.Contains(WalletTools.DailyTestnetEnv, ex.Message);
        Assert.Equal(0UL, SpendLedger.SpentToday(WalletNetwork.Mainnet));
        SpendLedger.Release(Tn, 60_000_000);
        SpendLedger.Reserve(Tn, 100_000_000, max);
        Assert.True(File.Exists(SpendLedger.PathFor));
    }

    [Fact]
    public void F3_daily_limit_defaults_and_env()
    {
        using var h = new TempHome();
        Assert.Equal(50 * 100_000_000UL, WalletTools.DailyMaxSompi(Tn));
        Assert.Equal(2 * 100_000_000UL, WalletTools.DailyMaxSompi(WalletNetwork.Mainnet));
        h.Set(WalletTools.DailyTestnetEnv, "3.5");
        Assert.Equal(350_000_000UL, WalletTools.DailyMaxSompi(Tn));
    }

    [Fact]
    public void F3_corrupt_ledger_fails_closed()
    {
        using var h = new TempHome();
        File.WriteAllText(SpendLedger.PathFor, "{not json");
        Assert.Throws<UserException>(() => SpendLedger.Reserve(Tn, 1, 100));
    }

    // ---------- F4: node allowlist ----------

    [Fact]
    public async Task F4_arbitrary_node_is_rejected()
    {
        using var h = new TempHome();
        h.Set("KASPAMY_PASSWORD", Vectors.Password);
        foreach (var bad in new[] { "ws://127.0.0.1:18210", "wss://intranet.lan/x", "wss://evil.example/kaspa/testnet-10/wrpc/json" })
        {
            Assert.Contains("allowlist", await WalletTools.EstimateFee(null, null, null, bad));
            Assert.Contains("allowlist", await WalletTools.GetBalance(null, bad));
            Assert.Contains("allowlist", await WalletTools.SendKas(Vectors.Testnet, "1", null, true, bad));
        }
        Assert.Equal(NodeEndpoints.Testnet10Defaults[0], WalletTools.CheckNode(Tn, NodeEndpoints.Testnet10Defaults[0]));
        Assert.Null(WalletTools.CheckNode(Tn, null));
        h.Set(WalletTools.NodesEnv, "ws://127.0.0.1:18210, wss://my.node/x");
        Assert.Equal("ws://127.0.0.1:18210", WalletTools.CheckNode(Tn, "ws://127.0.0.1:18210"));
        Assert.Equal("wss://my.node/x", WalletTools.CheckNode(Tn, "wss://my.node/x"));
    }

    // ---------- F5: import disabled by default ----------

    [Fact]
    public async Task F5_wallet_import_disabled_unless_env()
    {
        using var h = new TempHome();
        h.Set("KASPAMY_PASSWORD", Vectors.Password);
        var r = P(await WalletTools.WalletImport(Vectors.Abandon, null));
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Contains(WalletTools.AllowImportEnv, r.GetProperty("error").GetString());
        Assert.DoesNotContain("abandon", r.ToString());
        Assert.False(WalletService.Exists(Tn));
        h.Set(WalletTools.AllowImportEnv, "true"); // only "1" enables
        Assert.False(P(await WalletTools.WalletImport(Vectors.Abandon, null)).GetProperty("ok").GetBoolean());
        h.Set(WalletTools.AllowImportEnv, "1");
        Assert.True(P(await WalletTools.WalletImport(Vectors.Abandon, null)).GetProperty("ok").GetBoolean());
    }

    // ---------- F6: cap includes fee ----------

    [Fact]
    public void F6_cap_includes_fee()
    {
        using var h = new TempHome();
        WalletTools.CheckCap(Tn, 10 * 100_000_000UL, 0);
        var ex = Assert.Throws<UserException>(() => WalletTools.CheckCap(Tn, 10 * 100_000_000UL, 1));
        Assert.Contains("fee", ex.Message);
        WalletTools.CheckCap(Tn, 9 * 100_000_000UL, 50_000_000);
        Assert.Throws<UserException>(() => WalletTools.CheckCap(WalletNetwork.Mainnet, 99_999_000, 2_000));
    }

    // ---------- F7: no raw internal errors ----------

    [Fact]
    public async Task F7_cli_errors_are_clean()
    {
        using var h = new TempHome();
        var io = new FakeIO(); io.Env["KASPAMY_PASSWORD"] = Vectors.Password;
        Assert.Equal(64, await CliApp.RunAsync(new[] { "create", "--words", "abc", "--json" }, io));
        var err = P(io.Stdout.Last()).GetProperty("error").GetString()!;
        Assert.Equal("--words must be 12 or 24.", err);

        await CliApp.RunAsync(new[] { "import", "--json" }, new FakeIO { Env = { ["KASPAMY_PASSWORD"] = Vectors.Password, ["KASPAMY_MNEMONIC"] = Vectors.Abandon } });
        io = new FakeIO { IsInteractive = true };
        io.Lines.Enqueue(CliApp.ExportConfirmPhrase); io.Secrets.Enqueue("");
        Assert.Equal(1, await CliApp.RunAsync(new[] { "export-mnemonic", "--json" }, io));
        err = P(io.Stdout.Last()).GetProperty("error").GetString()!;
        Assert.DoesNotContain("Parameter", err);
        Assert.DoesNotContain("Argon2", err);
        Assert.DoesNotContain("   at ", io.All);
    }

    [Fact]
    public void F7_error_text_hides_internals()
    {
        Assert.Equal("Bad thing", ErrorText.Safe(new ArgumentException("Bad thing", "password")));
        Assert.Equal("Unexpected error (NullReferenceException).", ErrorText.Safe(new NullReferenceException("internal detail")));
        Assert.Equal("Unexpected error (Exception).", ErrorText.Safe(new Exception("outer", new Exception("inner secret"))));
        Assert.DoesNotContain("abandon abandon", ErrorText.Safe(new FormatException("x " + Vectors.Abandon)));
    }

    // ---------- F8: tamper vs wrong password ----------

    [Theory]
    [InlineData("Network", "Mainnet")]
    [InlineData("Argon2Iterations", 2)]
    [InlineData("Ciphertext", "flip")]
    [InlineData("Address", "kaspatest:qypqwzn4hawgas3ty6yeg4p9pw5m0j4j3ls7fl6yhyuuy4ny8aakjmc9fy8yt8n")]
    public void F8_tampered_file_is_reported_as_modified(string field, object value)
    {
        using var h = new TempHome();
        new WalletService(KdfParameters.TestOnly).Import(Tn, Vectors.Abandon, Vectors.Password);
        EditWallet(o =>
        {
            if (field == "Ciphertext") { var b = Convert.FromBase64String(o["Ciphertext"]!.GetValue<string>()); b[0] ^= 1; o["Ciphertext"] = Convert.ToBase64String(b); }
            else o[field] = JsonValue.Create(value);
        });
        var path = KaspaMyPaths.WalletPath(Tn);
        if (field == "Network")
        {
            // Network edit moves the file's meaning; read it directly.
            var ex0 = Assert.ThrowsAny<IOException>(() => WalletKeystore.Decrypt(WalletKeystore.Load(path), Vectors.Password));
            Assert.Contains("modified", ex0.Message);
            return;
        }
        var ex = Assert.Throws<KeystoreTamperedException>(() => WalletService.Unlock(Tn, Vectors.Password));
        Assert.Contains("modified or damaged", ex.Message);
    }

    [Fact]
    public void F8_wrong_password_is_reported_as_wrong_password()
    {
        using var h = new TempHome();
        new WalletService(KdfParameters.TestOnly).Import(Tn, Vectors.Abandon, Vectors.Password);
        var ex = Assert.Throws<WrongPasswordException>(() => WalletService.Unlock(Tn, "not the password"));
        Assert.Equal("Wrong password.", ex.Message);
    }

    [Fact]
    public void F8_legacy_file_without_checksum_still_opens_and_uses_combined_message()
    {
        using var h = new TempHome();
        new WalletService(KdfParameters.TestOnly).Import(Tn, Vectors.Abandon, Vectors.Password);
        EditWallet(o => o.Remove("Checksum"));
        using (var acc = WalletService.Unlock(Tn, Vectors.Password)) Assert.Equal(Vectors.Testnet, acc.Address);
        var ex = Assert.Throws<WrongPasswordException>(() => WalletService.Unlock(Tn, "not the password"));
        Assert.Contains("modified", ex.Message);
    }

    // ---------- F9: export always prompts for the password ----------

    [Fact]
    public async Task F9_export_ignores_env_password()
    {
        using var h = new TempHome();
        new WalletService(KdfParameters.TestOnly).Import(Tn, Vectors.Abandon, Vectors.Password);
        var io = new FakeIO { IsInteractive = true, Env = { ["KASPAMY_PASSWORD"] = Vectors.Password } };
        io.Lines.Enqueue(CliApp.ExportConfirmPhrase); // no typed password queued
        Assert.NotEqual(0, await CliApp.RunAsync(new[] { "export-mnemonic", "--json" }, io));
        Assert.DoesNotContain("abandon", io.All);
        Assert.Contains(io.Stderr, s => s.Contains("password", StringComparison.OrdinalIgnoreCase));

        io = new FakeIO { IsInteractive = true, Env = { ["KASPAMY_PASSWORD"] = Vectors.Password } };
        io.Lines.Enqueue(CliApp.ExportConfirmPhrase); io.Secrets.Enqueue(Vectors.Password);
        Assert.Equal(0, await CliApp.RunAsync(new[] { "export-mnemonic", "--json" }, io));
        Assert.Contains(Vectors.Abandon, io.AllOut);
    }

    // ---------- F10: uppercase bech32 ----------

    [Fact]
    public void F10_all_uppercase_address_is_valid_mixed_case_is_not()
    {
        var up = AddressValidator.Validate(Vectors.Mainnet.ToUpperInvariant());
        Assert.True(up.Valid);
        Assert.Equal("mainnet", up.Network);
        Assert.True(AddressValidator.Validate(Vectors.Testnet.ToUpperInvariant(), Tn).MatchesExpectedNetwork);
        Assert.Equal(Vectors.Testnet, AddressValidator.Normalize(Vectors.Testnet.ToUpperInvariant()));
        var mixed = "KASPA:" + Vectors.Mainnet["kaspa:".Length..];
        Assert.False(AddressValidator.Validate(mixed).Valid);
    }

    // ---------- F11: help text ----------

    [Fact]
    public async Task F11_help_text_is_accurate()
    {
        var io = new FakeIO();
        Assert.Equal(0, await CliApp.RunAsync(new[] { "help" }, io));
        var u = io.AllOut;
        Assert.DoesNotContain("default %APPDATA%/KaspaMy", u);
        Assert.Contains("~/.config/KaspaMy", u);
        Assert.DoesNotContain("send-max", u);
        Assert.Contains("KASPAMY_PASSWORD is ignored", u);
        Assert.Contains("status", u);
        // --send-max is no longer a recognised flag
        var io2 = new FakeIO();
        Assert.Equal(64, await CliApp.RunAsync(new[] { "send", Vectors.Testnet, "1", "--send-max", "--json" }, io2));
    }
}
