// KaspaMy (MIT). Copyright (c) 2026 jun chen.
using System.Text.Json;
using KaspaMy.Core.Transfer;
using KaspaMy.Core;

namespace KaspaMy.Mcp;

/// <summary>
/// F3: persistent per-day (local calendar day) total of KAS sent through the MCP server, amount + fee, per network.
/// Stored in KASPAMY_HOME/mcp-spend.json under a cross-process lock, so it bounds every session and every server
/// process sharing the wallet directory. Not a substitute for human approval in the MCP client.
/// </summary>
public static class SpendLedger
{
    private sealed class Data { public string Day { get; set; } = ""; public Dictionary<string, ulong> Sompi { get; set; } = new(); }

    public static string PathFor => System.IO.Path.Combine(KaspaMyPaths.Home, "mcp-spend.json");
    private static string Today => DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static Data Read()
    {
        try
        {
            var d = File.Exists(PathFor) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(PathFor)) : null;
            return d is not null && d.Day == Today ? d : new Data { Day = Today };
        }
        catch (JsonException)
        {
            // Unreadable ledger: fail closed (treat as fully spent) rather than resetting the limit.
            throw new UserException($"The MCP spend ledger {PathFor} is unreadable; refusing to send. Fix or delete it manually.");
        }
    }

    public static ulong SpentToday(WalletNetwork n) => Read().Sompi.GetValueOrDefault(n.Id());

    /// <summary>Atomically checks the daily limit and reserves <paramref name="sompi"/>. Returns the new total.</summary>
    public static ulong Reserve(WalletNetwork n, ulong sompi, ulong dailyMax)
    {
        using var _ = WalletLock.Acquire(PathFor);
        var d = Read();
        var spent = d.Sompi.GetValueOrDefault(n.Id());
        if (spent + sompi > dailyMax)
            throw new UserException($"Daily MCP limit reached on {n.Id()}: already sent {Kas.Format(spent)} KAS today, this transfer needs {Kas.Format(sompi)} KAS " +
                $"(amount + fee), limit {Kas.Format(dailyMax)} KAS. Configure with {(n == WalletNetwork.Mainnet ? WalletTools.DailyMainnetEnv : WalletTools.DailyTestnetEnv)}.");
        d.Sompi[n.Id()] = spent + sompi;
        Write(d);
        return spent + sompi;
    }

    /// <summary>Releases a reservation after a broadcast that definitely failed.</summary>
    public static void Release(WalletNetwork n, ulong sompi)
    {
        using var _ = WalletLock.Acquire(PathFor);
        var d = Read();
        var spent = d.Sompi.GetValueOrDefault(n.Id());
        d.Sompi[n.Id()] = spent > sompi ? spent - sompi : 0;
        Write(d);
    }

    private static void Write(Data d)
    {
        Directory.CreateDirectory(KaspaMyPaths.Home);
        var tmp = $"{PathFor}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(d));
        File.Move(tmp, PathFor, overwrite: true);
    }
}
