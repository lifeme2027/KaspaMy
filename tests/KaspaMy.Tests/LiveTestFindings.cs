// KaspaMy (MIT). Copyright (c) 2026 jun chen.
// Regression tests for issues seen in the 2026-11-01 mainnet live test of 58be59d.
using KaspaMy.Cli;
using KaspaMy.Core;
using KaspaMy.Mcp;
using Xunit;

namespace KaspaMy.Tests;

public class LiveTestFindings
{
    [Fact]
    public async Task L1_precheck_cap_message_does_not_claim_a_zero_fee()
    {
        using var h = new TempHome();
        h.Set("KASPAMY_PASSWORD", Vectors.Password);
        h.Set(WalletTools.AllowMainnetEnv, "1");
        var r = await WalletTools.SendKas(Vectors.Mainnet, "1.5", "mainnet", true, null);
        Assert.Contains("Amount 1.50000000 KAS (before fee) exceeds the per-transaction limit of 1.00000000 KAS on mainnet", r);
        Assert.DoesNotContain("fee 0.00000000", r);
        var ex = Assert.Throws<UserException>(() => WalletTools.CheckCap(WalletNetwork.Mainnet, 99_900_000, 203_600));
        Assert.Contains("+ fee 0.00203600 KAS", ex.Message);
    }

    [Fact]
    public async Task L2_json_messages_keep_plus_sign_readable()
    {
        using var h = new TempHome();
        h.Set("KASPAMY_PASSWORD", Vectors.Password);
        var mcp = await WalletTools.SendKas(Vectors.Testnet, "10.5", null, true, null);
        Assert.DoesNotContain("\\u002B", mcp);
        Assert.Contains("(before fee)", mcp);
        var ex = WalletTools.Fail("amount + fee");
        Assert.Contains("amount + fee", ex);

        var io = new FakeIO();
        await CliApp.RunAsync(new[] { "validate", "x+y", "--json" }, io);
        Assert.DoesNotContain("\\u002B", io.AllOut);
    }
}
