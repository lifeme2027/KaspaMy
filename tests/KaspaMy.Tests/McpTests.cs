// KaspaMy (MIT). Copyright (c) 2026 jun chen.
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KaspaMy.Mcp;
using Xunit;

namespace KaspaMy.Tests;

public class McpToolTests
{
    private static JsonElement P(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public async Task Create_never_returns_mnemonic()
    {
        using var h = new TempHome();
        h.Set("KASPAMY_PASSWORD", Vectors.Password);
        var lines = new List<string>();
        KaspaMy.Core.WalletLog.Sink = lines.Add;
        try
        {
            var r = await WalletTools.WalletCreate(null);
            Assert.True(P(r).GetProperty("ok").GetBoolean());
            Assert.Contains("kaspamy export-mnemonic", r);
            var m = KaspaMy.Core.WalletService.ExportMnemonic(KaspaMy.Core.WalletNetwork.Testnet10, Vectors.Password).Reveal();
            Assert.DoesNotContain(string.Join(' ', m.Split(' ').Take(2)), r);
            Assert.DoesNotContain(string.Join(' ', m.Split(' ').Take(2)), string.Join("\n", lines));
        }
        finally { KaspaMy.Core.WalletLog.Sink = null; }
    }

    [Fact]
    public async Task Import_does_not_echo_mnemonic_even_on_error()
    {
        using var h = new TempHome();
        h.Set("KASPAMY_PASSWORD", Vectors.Password);
        h.Set(WalletTools.AllowImportEnv, "1");
        var lines = new List<string>();
        KaspaMy.Core.WalletLog.Sink = lines.Add;
        try
        {
            var ok = await WalletTools.WalletImport(Vectors.Abandon, null);
            Assert.Contains(Vectors.Testnet, ok);
            var bad = await WalletTools.WalletImport(Vectors.Abandon.Replace("about", "zebra"), "mainnet");
            var dup = await WalletTools.WalletImport(Vectors.Abandon, null);
            Assert.False(P(dup).GetProperty("ok").GetBoolean());
            foreach (var s in new[] { ok, bad, dup, string.Join("\n", lines) })
                Assert.DoesNotContain("abandon abandon", s);
        }
        finally { KaspaMy.Core.WalletLog.Sink = null; }
    }

    [Fact]
    public async Task Requires_password_env()
    {
        using var h = new TempHome();
        var r = P(await WalletTools.WalletCreate(null));
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Contains("KASPAMY_PASSWORD", r.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Mainnet_disabled_by_default()
    {
        using var h = new TempHome();
        h.Set("KASPAMY_PASSWORD", Vectors.Password);
        foreach (var r in new[] { await WalletTools.WalletCreate("mainnet"), await WalletTools.SendKas(Vectors.Mainnet, "0.1", "mainnet", true, null), await WalletTools.GetBalance("mainnet", null) })
            Assert.Contains("KASPAMY_MCP_ALLOW_MAINNET", r);
    }

    [Fact]
    public async Task Send_limits_and_confirm()
    {
        using var h = new TempHome();
        h.Set("KASPAMY_PASSWORD", Vectors.Password);
        Assert.Contains("confirm=true", await WalletTools.SendKas(Vectors.Testnet, "1", null, false, "ws://127.0.0.1:1"));
        Assert.Contains("per-transaction limit", await WalletTools.SendKas(Vectors.Testnet, "10.00000001", null, true, "ws://127.0.0.1:1"));
        h.Set(WalletTools.MaxTestnetEnv, "0.5");
        Assert.Contains("per-transaction limit", await WalletTools.SendKas(Vectors.Testnet, "1", null, true, "ws://127.0.0.1:1"));
        Assert.Equal(100_000_000UL, WalletTools.MaxSompi(KaspaMy.Core.WalletNetwork.Mainnet));
        Assert.Contains("mainn", await WalletTools.SendKas(Vectors.Mainnet, "0.1", null, true, null));
    }

    [Fact]
    public async Task Validate_tool()
    {
        var r = P(await WalletTools.ValidateAddress(Vectors.Testnet, null));
        Assert.Equal("testnet-10", r.GetProperty("data").GetProperty("network").GetString());
    }
}

/// <summary>Runs the real stdio server as a child process.</summary>
public class McpProcessTests
{
    private static string ServerDll => Path.Combine(AppContext.BaseDirectory, "kaspamy-mcp.dll");

    private static async Task<(List<JsonNode> responses, string stdout, string stderr)> Session(string home, params (string method, object prms)[] requests)
    {
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add(ServerDll);
        psi.Environment["KASPAMY_HOME"] = home;
        psi.Environment["KASPAMY_PASSWORD"] = Vectors.Password;
        psi.Environment["KASPAMY_VERBOSE"] = "1";
        psi.Environment["KASPAMY_MCP_ALLOW_IMPORT"] = "1";
        psi.Environment.Remove("KASPAMY_MCP_ALLOW_MAINNET");
        using var p = Process.Start(psi)!;
        var stderr = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };
        p.BeginErrorReadLine();
        var all = new StringBuilder();
        var responses = new List<JsonNode>();
        async Task<JsonNode> Call(object msg, bool expectReply)
        {
            await p.StandardInput.WriteLineAsync(JsonSerializer.Serialize(msg));
            await p.StandardInput.FlushAsync();
            if (!expectReply) return null!;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            while (true)
            {
                var line = await p.StandardOutput.ReadLineAsync(cts.Token) ?? throw new IOException("server closed stdout; stderr: " + stderr);
                all.AppendLine(line);
                var n = JsonNode.Parse(line)!;
                if (n["id"] is not null) return n;
            }
        }
        await Call(new { jsonrpc = "2.0", id = 0, method = "initialize", @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "1" } } }, true);
        await Call(new { jsonrpc = "2.0", method = "notifications/initialized" }, false);
        var id = 1;
        foreach (var r in requests)
        {
            var (method, prms) = r;
            responses.Add(await Call(new { jsonrpc = "2.0", id = id++, method, @params = prms }, true));
        }
        p.StandardInput.Close();
        if (!p.WaitForExit(15000)) p.Kill(true);
        p.WaitForExit();
        return (responses, all.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task Tools_list_and_no_secrets_on_the_wire()
    {
        using var h = new TempHome();
        var (rs, stdout, stderr) = await Session(h.Dir,
            ("tools/list", (object)new { }),
            ("tools/call", (object)new { name = "wallet_import", arguments = new { mnemonic = Vectors.Abandon } }),
            ("tools/call", (object)new { name = "wallet_status", arguments = new { } }),
            ("tools/call", (object)new { name = "get_address", arguments = new { } }),
            ("tools/call", (object)new { name = "wallet_import", arguments = new { mnemonic = Vectors.Abandon } }));

        var names = rs[0]["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToHashSet();
        foreach (var n in new[] { "wallet_create", "wallet_import", "wallet_status", "get_address", "get_balance", "estimate_fee", "send_kas", "validate_address" })
            Assert.Contains(n, names);
        var send = rs[0]["result"]!["tools"]!.AsArray().First(t => t!["name"]!.GetValue<string>() == "send_kas")!;
        Assert.DoesNotContain("password", send["inputSchema"]!.ToJsonString(), StringComparison.OrdinalIgnoreCase);

        Assert.Contains(Vectors.Testnet, rs[1].ToJsonString());
        Assert.Contains(Vectors.Testnet, rs[3].ToJsonString());
        Assert.Contains("already exists", rs[4].ToJsonString());

        // The request carried the mnemonic, but no response or log line may echo it.
        Assert.DoesNotContain("abandon", stdout);
        Assert.DoesNotContain("abandon", stderr);
        Assert.DoesNotContain(Vectors.Password, stdout + stderr);
    }

    [Fact]
    public async Task Create_over_stdio_returns_no_mnemonic()
    {
        using var h = new TempHome();
        var (rs, stdout, stderr) = await Session(h.Dir, ("tools/call", (object)new { name = "wallet_create", arguments = new { words = 12 } }));
        Assert.Contains("export-mnemonic", rs[0].ToJsonString());
        var m = KaspaMy.Core.WalletService.ExportMnemonic(KaspaMy.Core.WalletNetwork.Testnet10, Vectors.Password).Reveal();
        // Single words could collide with prose, so check every 2-word window.
        var words = m.Split(' ');
        for (var i = 0; i + 1 < words.Length; i++)
        {
            var pair = words[i] + " " + words[i + 1];
            Assert.DoesNotContain(pair, stdout);
            Assert.DoesNotContain(pair, stderr);
        }
    }
}
