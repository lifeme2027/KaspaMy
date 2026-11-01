// KaspaMy (MIT). Copyright (c) 2026 jun chen.
namespace KaspaMy.Core;

/// <summary>
/// F1: exclusive cross-process lock on "&lt;wallet&gt;.lock" (FileShare.None → flock on Unix, share-mode on Windows).
/// Held around every wallet create/import and around MCP spend accounting. The lock file is left in place on purpose
/// (deleting it would let two processes lock different inodes).
/// </summary>
public sealed class WalletLock : IDisposable
{
    private readonly FileStream _fs;
    private WalletLock(FileStream fs) => _fs = fs;

    public static WalletLock Acquire(string targetPath, TimeSpan? timeout = null)
    {
        var lockPath = targetPath + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(lockPath))!);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            try
            {
                return new WalletLock(new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(Random.Shared.Next(20, 60));
            }
            catch (IOException)
            {
                throw new IOException("The wallet is busy (another KaspaMy process holds its lock). Try again.");
            }
        }
    }

    public void Dispose() => _fs.Dispose();
}
