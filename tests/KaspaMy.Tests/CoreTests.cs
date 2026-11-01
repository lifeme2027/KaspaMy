// KaspaMy (MIT). Copyright (c) 2026 jun chen.
using KaspaMy.Core;
using KaspaMy.Core.Keystore;
using Xunit;

namespace KaspaMy.Tests;

public class CoreTests
{
    [Theory]
    [InlineData(WalletNetwork.Mainnet, Vectors.Mainnet)]
    [InlineData(WalletNetwork.Testnet10, Vectors.Testnet)]
    public void Derivation_vector(WalletNetwork n, string expected)
        => Assert.Equal(expected, HdAccount.DeriveAddress(Vectors.Abandon, n));

    [Fact]
    public void Keystore_round_trip()
    {
        var f = WalletKeystore.Encrypt(Vectors.Abandon, Vectors.Password, WalletNetwork.Testnet10, KdfParameters.TestOnly);
        Assert.Equal(Vectors.Abandon, WalletKeystore.Decrypt(f, Vectors.Password));
        using var acc = WalletKeystore.Unlock(f, Vectors.Password);
        Assert.Equal(Vectors.Testnet, acc.Address);
    }

    [Fact]
    public void Keystore_file_round_trip_has_no_plaintext()
    {
        using var h = new TempHome();
        var info = new WalletService(KdfParameters.TestOnly).Import(WalletNetwork.Testnet10, Vectors.Abandon, Vectors.Password);
        var text = File.ReadAllText(info.KeystorePath);
        Assert.DoesNotContain("abandon", text);
        Assert.Equal(Vectors.Testnet, WalletService.Status(WalletNetwork.Testnet10).Address);
        Assert.Equal(Vectors.Abandon, WalletService.ExportMnemonic(WalletNetwork.Testnet10, Vectors.Password).Reveal());
    }

    [Fact]
    public void Keystore_wrong_password()
    {
        var f = WalletKeystore.Encrypt(Vectors.Abandon, Vectors.Password, WalletNetwork.Testnet10, KdfParameters.TestOnly);
        Assert.Throws<WrongPasswordException>(() => WalletKeystore.Decrypt(f, "not the password"));
    }

    [Fact]
    public void Refuses_to_overwrite_wallet()
    {
        using var h = new TempHome();
        var svc = new WalletService(KdfParameters.TestOnly);
        svc.Import(WalletNetwork.Testnet10, Vectors.Abandon, Vectors.Password);
        Assert.Throws<WalletExistsException>(() => svc.Create(WalletNetwork.Testnet10, Vectors.Password));
    }

    [Fact]
    public void Secret_text_never_prints()
    {
        var s = new SecretText(Vectors.Abandon);
        Assert.DoesNotContain("abandon", s.ToString());
        Assert.DoesNotContain("abandon", $"{s}");
        Assert.ThrowsAny<Exception>(() => System.Text.Json.JsonSerializer.Serialize(new { s }));
    }

    [Fact]
    public void Log_is_redacted()
    {
        var lines = new List<string>();
        WalletLog.Sink = lines.Add;
        try { WalletLog.Warn("oops " + Vectors.Abandon); } finally { WalletLog.Sink = null; }
        Assert.DoesNotContain("abandon abandon", string.Join("\n", lines));
    }

    [Theory]
    [InlineData(Vectors.Mainnet, true, "mainnet")]
    [InlineData(Vectors.Testnet, true, "testnet-10")]
    [InlineData("kaspa:qqd6e65yefepe9wk0m9vuxdufxd80sphy67gwwd0vdaumzdt4tc9s3qt0lqex", false, null)]
    [InlineData("bitcoin:abc", false, null)]
    [InlineData("", false, null)]
    public void Address_validation(string addr, bool valid, string? net)
    {
        var v = AddressValidator.Validate(addr);
        Assert.Equal(valid, v.Valid);
        if (valid) Assert.Equal(net, v.Network);
    }

    [Fact]
    public void Address_network_mismatch()
    {
        var v = AddressValidator.Validate(Vectors.Mainnet, WalletNetwork.Testnet10);
        Assert.True(v.Valid);
        Assert.False(v.MatchesExpectedNetwork);
    }

    [Theory]
    [InlineData("1", 100_000_000UL)]
    [InlineData("0.00000001", 1UL)]
    [InlineData("12.5", 1_250_000_000UL)]
    public void Kas_parse(string s, ulong sompi) => Assert.Equal(sompi, KaspaMy.Core.Transfer.Kas.ParseKas(s));

    [Fact]
    public void Paths_honour_home_override()
    {
        using var h = new TempHome();
        Assert.Equal(Path.Combine(h.Dir, "wallet.testnet-10.json"), KaspaMyPaths.WalletPath(WalletNetwork.Testnet10));
        Assert.Equal(WalletNetwork.Testnet10, WalletNetworkExtensions.Default);
    }

    [Theory]
    [InlineData("wss://x.example/kaspa/testnet-10/wrpc/json", true)]
    [InlineData("ws://127.0.0.1:18210", true)]
    [InlineData("http://x.example", false)]
    [InlineData("not a url", false)]
    public void Node_url_validation(string url, bool ok)
    {
        if (ok) NodeEndpoints.ValidateUrl(url); else Assert.ThrowsAny<Exception>(() => NodeEndpoints.ValidateUrl(url));
    }
}
