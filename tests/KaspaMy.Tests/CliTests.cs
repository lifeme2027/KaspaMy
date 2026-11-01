// KaspaMy (MIT). Copyright (c) 2026 jun chen.
using System.Text.Json;
using KaspaMy.Cli;
using Xunit;

namespace KaspaMy.Tests;

public class CliTests
{
    private static async Task<(int code, FakeIO io, JsonElement json)> Run(FakeIO io, params string[] args)
    {
        var code = await CliApp.RunAsync(args.Append("--json").ToArray(), io);
        var doc = JsonDocument.Parse(io.Stdout.Last()).RootElement;
        return (code, io, doc);
    }

    private static FakeIO Io(bool interactive = false)
    {
        var io = new FakeIO { IsInteractive = interactive };
        io.Env["KASPAMY_PASSWORD"] = Vectors.Password;
        io.Env["KASPAMY_MNEMONIC"] = Vectors.Abandon;
        return io;
    }

    [Fact]
    public async Task Import_and_address_json_shape()
    {
        using var h = new TempHome();
        var (code, io, j) = await Run(Io(), "import");
        Assert.Equal(0, code);
        Assert.True(j.GetProperty("ok").GetBoolean());
        Assert.Equal("import", j.GetProperty("command").GetString());
        var d = j.GetProperty("data");
        Assert.Equal(Vectors.Testnet, d.GetProperty("address").GetString());
        Assert.Equal("testnet-10", d.GetProperty("network").GetString());
        Assert.DoesNotContain("abandon", io.All);

        (code, io, j) = await Run(Io(), "address");
        Assert.Equal(0, code);
        Assert.Equal(Vectors.Testnet, j.GetProperty("data").GetProperty("address").GetString());
    }

    [Fact]
    public async Task Create_never_prints_mnemonic()
    {
        using var h = new TempHome();
        var io = Io();
        io.Env.Remove("KASPAMY_MNEMONIC");
        var (code, _, j) = await Run(io, "create");
        Assert.Equal(0, code);
        Assert.StartsWith("kaspatest:", j.GetProperty("data").GetProperty("wallet").GetProperty("address").GetString());
        var mnemonic = KaspaMy.Core.WalletService.ExportMnemonic(KaspaMy.Core.WalletNetwork.Testnet10, Vectors.Password).Reveal();
        Assert.DoesNotContain(mnemonic, io.All);
        Assert.DoesNotContain(string.Join(' ', mnemonic.Split(' ').Take(3)), io.All);
    }

    [Fact]
    public async Task Validate_json_shape()
    {
        var (code, _, j) = await Run(new FakeIO(), "validate", Vectors.Mainnet);
        Assert.Equal(0, code);
        var d = j.GetProperty("data");
        Assert.True(d.GetProperty("valid").GetBoolean());
        Assert.Equal("mainnet", d.GetProperty("network").GetString());
    }

    [Fact]
    public async Task Error_json_shape()
    {
        using var h = new TempHome();
        var (code, _, j) = await Run(new FakeIO(), "address");
        Assert.Equal(1, code);
        Assert.False(j.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrEmpty(j.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Wrong_password_reports_error()
    {
        using var h = new TempHome();
        await Run(Io(), "import");
        var io = Io(); io.Env["KASPAMY_PASSWORD"] = "wrong password!";
        var (code, _, j) = await Run(io, "export-mnemonic");
        Assert.NotEqual(0, code);
        Assert.False(j.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Export_mnemonic_refuses_non_interactive()
    {
        using var h = new TempHome();
        await Run(Io(), "import");
        var (code, io, j) = await Run(Io(interactive: false), "export-mnemonic");
        Assert.Equal(64, code);
        Assert.DoesNotContain("abandon", io.All);
    }

    [Fact]
    public async Task Export_mnemonic_requires_typed_phrase()
    {
        using var h = new TempHome();
        await Run(Io(), "import");
        var io = Io(interactive: true); io.Lines.Enqueue("yes");
        var (code, _, _) = await Run(io, "export-mnemonic");
        Assert.Equal(2, code);
        Assert.DoesNotContain("abandon", io.All);

        io = Io(interactive: true); io.Lines.Enqueue(CliApp.ExportConfirmPhrase); io.Secrets.Enqueue(Vectors.Password);
        (code, _, _) = await Run(io, "export-mnemonic");
        Assert.Equal(0, code);
        Assert.Contains(Vectors.Abandon, io.AllOut);
    }

    [Fact]
    public async Task Send_requires_yes_when_non_interactive()
    {
        using var h = new TempHome();
        await Run(Io(), "import");
        var (code, io, j) = await Run(Io(), "send", Vectors.Testnet, "1");
        Assert.Equal(64, code);
        Assert.False(j.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Send_mainnet_requires_explicit_network()
    {
        using var h = new TempHome();
        var (code, _, j) = await Run(Io(), "send", Vectors.Mainnet, "1", "--yes");
        Assert.NotEqual(0, code);
        Assert.False(j.GetProperty("ok").GetBoolean());
    }
}
