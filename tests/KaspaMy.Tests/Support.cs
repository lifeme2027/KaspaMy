// KaspaMy (MIT). Copyright (c) 2026 jun chen.
using KaspaMy.Cli;
using KaspaMy.Core.Keystore;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace KaspaMy.Tests;

public static class Vectors
{
    public const string Abandon = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    public const string Mainnet = "kaspa:qqd6e65yefepe9wk0m9vuxdufxd80sphy67gwwd0vdaumzdt4tc9s3qt0lqeh";
    public const string Testnet = "kaspatest:qqd6e65yefepe9wk0m9vuxdufxd80sphy67gwwd0vdaumzdt4tc9ssxd5s7gn";
    public const string Password = "correct horse battery";
}

/// <summary>Isolated KASPAMY_HOME + env for one test; restores everything on dispose.</summary>
public sealed class TempHome : IDisposable
{
    private readonly Dictionary<string, string?> _saved = new();
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "kaspamy-test-" + Guid.NewGuid().ToString("N"));
    public TempHome()
    {
        Directory.CreateDirectory(Dir);
        Set("KASPAMY_HOME", Dir);
        foreach (var k in new[] { "KASPAMY_PASSWORD", "KASPAMY_MNEMONIC", "KASPAMY_NODE", "KASPAMY_MCP_ALLOW_MAINNET", "KASPAMY_MCP_MAX_KAS_TESTNET", "KASPAMY_MCP_MAX_KAS_MAINNET", "KASPAMY_MCP_ALLOW_IMPORT", "KASPAMY_MCP_NODES", "KASPAMY_MCP_DAILY_MAX_KAS_TESTNET", "KASPAMY_MCP_DAILY_MAX_KAS_MAINNET" }) Set(k, null);
        CliApp.KdfOverride = KdfParameters.TestOnly;
        KaspaMy.Mcp.WalletTools.KdfOverride = KdfParameters.TestOnly;
    }
    public void Set(string k, string? v) { if (!_saved.ContainsKey(k)) _saved[k] = Environment.GetEnvironmentVariable(k); Environment.SetEnvironmentVariable(k, v); }
    public void Dispose()
    {
        foreach (var (k, v) in _saved) Environment.SetEnvironmentVariable(k, v);
        try { Directory.Delete(Dir, true); } catch { }
    }
}

public sealed class FakeIO : IConsoleIO
{
    public List<string> Stdout { get; } = new();
    public List<string> Stderr { get; } = new();
    public Queue<string> Lines { get; } = new();
    public Queue<string> Secrets { get; } = new();
    public Dictionary<string, string> Env { get; } = new();
    public bool IsInteractive { get; set; }
    public void Out(string text) => Stdout.Add(text);
    public void Err(string text) => Stderr.Add(text);
    public string? ReadLine(string prompt) { Stderr.Add(prompt); return Lines.Count > 0 ? Lines.Dequeue() : null; }
    public string? ReadSecret(string prompt) { Stderr.Add(prompt); return Secrets.Count > 0 ? Secrets.Dequeue() : null; }
    public string? GetEnv(string name) => Env.TryGetValue(name, out var v) ? v : Environment.GetEnvironmentVariable(name);
    public string AllOut => string.Join("\n", Stdout);
    public string All => string.Join("\n", Stdout.Concat(Stderr));
}
