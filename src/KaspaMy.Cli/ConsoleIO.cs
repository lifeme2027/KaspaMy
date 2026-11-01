// KaspaMy (MIT). Copyright (c) 2026 jun chen.
namespace KaspaMy.Cli;

/// <summary>Console abstraction so tests can drive the CLI without a terminal.</summary>
public interface IConsoleIO
{
    void Out(string text);
    void Err(string text);
    string? ReadLine(string prompt);
    string? ReadSecret(string prompt);
    bool IsInteractive { get; }
    string? GetEnv(string name);
}

public sealed class SystemConsole : IConsoleIO
{
    public void Out(string text) => Console.Out.WriteLine(text);
    public void Err(string text) => Console.Error.WriteLine(text);
    public bool IsInteractive => !Console.IsInputRedirected;
    public string? GetEnv(string name) => Environment.GetEnvironmentVariable(name);

    public string? ReadLine(string prompt) { Console.Error.Write(prompt); return Console.ReadLine(); }

    public string? ReadSecret(string prompt)
    {
        Console.Error.Write(prompt);
        if (Console.IsInputRedirected) return Console.ReadLine();
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
        Console.Error.WriteLine();
        return sb.ToString();
    }
}
