// KaspaMy (MIT). Copyright (c) 2026 jun chen.
namespace KaspaMy.Core;

/// <summary>
/// Wallet file location: %APPDATA%\KaspaMy\wallet.&lt;network&gt;.json (Environment.SpecialFolder.ApplicationData,
/// i.e. ~/.config/KaspaMy on Linux). KASPAMY_HOME overrides the directory (tests, portable use).
/// </summary>
public static class KaspaMyPaths
{
    public const string HomeEnv = "KASPAMY_HOME";

    public static string Home
    {
        get
        {
            var o = Environment.GetEnvironmentVariable(HomeEnv);
            if (!string.IsNullOrWhiteSpace(o)) return Path.GetFullPath(o);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "KaspaMy");
        }
    }

    public static string WalletPath(WalletNetwork n) => Path.Combine(Home, $"wallet.{n.Id()}.json");
}
