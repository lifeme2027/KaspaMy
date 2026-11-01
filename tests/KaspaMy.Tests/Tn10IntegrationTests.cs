// KaspaMy (MIT). Copyright (c) 2026 jun chen.
using KaspaMy.Core;
using KaspaMy.Core.Transfer;
using Xunit;
using Xunit.Abstractions;

namespace KaspaMy.Tests;

/// <summary>Live testnet-10 check. Runs only when KASPAMY_TN10_IT=1 (optional KASPAMY_TN10_NODE). Read-only: never sends.</summary>
public class Tn10IntegrationTests
{
    private readonly ITestOutputHelper _out;
    public Tn10IntegrationTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public async Task Connect_balance_and_fee_for_fresh_address()
    {
        if (Environment.GetEnvironmentVariable("KASPAMY_TN10_IT") != "1") { _out.WriteLine("skipped: set KASPAMY_TN10_IT=1"); return; }
        var mnemonic = MnemonicService.Generate(12);
        using var acc = HdAccount.FromMnemonic(mnemonic, WalletNetwork.Testnet10);
        var (rpc, url, st) = await NodeEndpoints.ConnectAsync(WalletNetwork.Testnet10, Environment.GetEnvironmentVariable("KASPAMY_TN10_NODE"));
        await using var _ = rpc;
        Assert.True(st.IsSynced);
        var coins = await rpc.GetCoinsAsync(acc.Address);
        Assert.Empty(coins);
        var fee = await SendService.EstimateAsync(rpc, WalletNetwork.Testnet10);
        Assert.True(fee.FeeSompi > 0);
        _out.WriteLine($"TN10 ok via {url}: daa={st.VirtualDaaScore} fresh balance=0 fee≈{fee.FeeKas} KAS");
    }
}
