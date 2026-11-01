// Adapted from KasNetWallet Core (WalletLog.cs), same author, MIT License. Part of KaspaMy (MIT).
// KaspaMy change: no log file. Lines go to stderr (or a test sink), always through SecretRedactor.
namespace KaspaMy.Core;

/// <summary>Redacting logger. Writes to stderr only (stdout is reserved for CLI output / MCP JSON-RPC).</summary>
public static class WalletLog
{
    private static readonly object Gate = new();
    /// <summary>For tests: capture redacted lines instead of writing to stderr.</summary>
    public static Action<string>? Sink { get; set; }
    /// <summary>When false (CLI default), Info lines are suppressed; warnings/errors are still written.</summary>
    public static bool Verbose { get; set; }

    public static void Info(string message) { if (Verbose || Sink is not null) Write("INFO", message); }
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {level} {SecretRedactor.Redact(message)}";
        lock (Gate)
        {
            if (Sink is not null) { Sink(line); return; }
            try { Console.Error.WriteLine(line); } catch (IOException) { }
        }
    }
}
