// KaspaMy (MIT). Copyright (c) 2026 jun chen.
using System.Globalization;
using System.Text.Json;
using KaspaMy.Core;
using KaspaMy.Core.Keystore;
using KaspaMy.Core.Transfer;

namespace KaspaMy.Mcp;

/// <summary>
/// F3/N2: persistent per-day (UTC calendar day) total of KAS sent through the MCP server, amount + fee, per network.
/// Stored in KASPAMY_HOME/mcp-spend.json (0600) under a cross-process lock, so it bounds every session and server process
/// sharing the wallet directory. Fail-closed rules (DECISIONS #24):
/// <list type="bullet">
/// <item>No ledger and no anchor file = first use: start at zero and create both.</item>
/// <item>Anchor present but ledger missing, ledger "null"/malformed, missing fields, or a Day in the future = refuse to send.</item>
/// <item>Day in the past = a new UTC day: reset to zero (the only legitimate reset).</item>
/// </list>
/// Someone with write access to KASPAMY_HOME can still delete both files; that attacker can also read the encrypted keystore
/// and, with the env password, already has the funds. Not a substitute for human approval in the MCP client.
/// </summary>
public static class SpendLedger
{
    private const int CurrentVersion = 2;
    private sealed class Data
    {
        public int Version { get; set; }
        public string? Day { get; set; }
        public Dictionary<string, ulong>? Sompi { get; set; }
    }

    public static string PathFor => Path.Combine(KaspaMyPaths.Home, "mcp-spend.json");
    /// <summary>Written together with the first ledger; never removed by KaspaMy. Its presence means "a ledger must exist".</summary>
    public static string AnchorPath => Path.Combine(KaspaMyPaths.Home, "mcp-spend.anchor");

    /// <summary>Overridable clock for tests.</summary>
    internal static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;
    private static DateOnly Today => DateOnly.FromDateTime(UtcNow());

    private static UserException Refuse(string why) =>
        new($"The MCP spend ledger {PathFor} {why}; refusing to send (fail-closed). If you are sure no MCP sends happened today (UTC), delete it and {Path.GetFileName(AnchorPath)} manually.");

    private static Data Read()
    {
        var today = Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!File.Exists(PathFor))
        {
            if (File.Exists(AnchorPath)) throw Refuse("is missing although it was created before");
            return new Data { Version = CurrentVersion, Day = today, Sompi = new() }; // genuine first use
        }
        Data? d;
        try { d = JsonSerializer.Deserialize<Data>(File.ReadAllText(PathFor)); }
        catch (JsonException) { throw Refuse("is unreadable"); }
        catch (IOException) { throw Refuse("cannot be read"); }
        if (d is null || d.Sompi is null || d.Day is null) throw Refuse("is empty or incomplete");
        if (!DateOnly.TryParseExact(d.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) throw Refuse("has an invalid date");
        // Ledgers written by 3447f53 (no Version) used the local date, which may be one day ahead of UTC: keep their spend.
        if (d.Version < CurrentVersion && day == Today.AddDays(1)) day = Today;
        if (day > Today) throw Refuse("is dated in the future");
        if (day < Today) return new Data { Version = CurrentVersion, Day = today, Sompi = new() }; // new UTC day
        d.Version = CurrentVersion; d.Day = today;
        return d;
    }

    public static ulong SpentToday(WalletNetwork n) => Read().Sompi!.GetValueOrDefault(n.Id());

    /// <summary>Atomically checks the daily limit and reserves <paramref name="sompi"/>. Returns the new total.</summary>
    public static ulong Reserve(WalletNetwork n, ulong sompi, ulong dailyMax)
    {
        using var _ = WalletLock.Acquire(PathFor);
        var d = Read();
        var spent = d.Sompi!.GetValueOrDefault(n.Id());
        if (sompi > dailyMax || spent > dailyMax - sompi) // overflow-safe form of spent + sompi > dailyMax
            throw new UserException($"Daily MCP limit reached on {n.Id()}: already sent {Kas.Format(spent)} KAS today (UTC), this transfer needs {Kas.Format(sompi)} KAS " +
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
        var spent = d.Sompi!.GetValueOrDefault(n.Id());
        d.Sompi[n.Id()] = spent > sompi ? spent - sompi : 0;
        Write(d);
    }

    private static void Write(Data d)
    {
        Directory.CreateDirectory(KaspaMyPaths.Home);
        if (!File.Exists(AnchorPath)) WalletKeystore.WriteOwnerOnly(AnchorPath, "KaspaMy MCP spend ledger anchor. Do not delete.\n");
        var tmp = $"{PathFor}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            WalletKeystore.WriteOwnerOnly(tmp, JsonSerializer.Serialize(d)); // 0600 + fsync
            File.Move(tmp, PathFor, overwrite: true);                       // atomic replace (we hold the lock)
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(PathFor, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } }
    }
}
