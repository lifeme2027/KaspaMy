// KaspaMy (MIT). Copyright (c) 2026 jun chen.
using KaspaMy.Core.Keystore;

namespace KaspaMy.Core;

/// <summary>
/// F7: turns an exception into a short, user-safe message. Only messages KaspaMy writes itself are shown; inner exceptions,
/// stack traces and third-party wording (e.g. "(Parameter 'password')") are never included. Everything is also redacted.
/// </summary>
public static class ErrorText
{
    public static string Safe(Exception ex)
    {
        var msg = ex switch
        {
            WrongPasswordException or KeystoreTamperedException or KeystoreCorruptException
                or WalletExistsException or WalletNotFoundException or NodeUnavailableException => ex.Message,
            UserException or KaspaMy.Core.Transfer.TransferException or KaspaMy.Core.Transfer.TransferStatusUnknownException => ex.Message,
            OperationCanceledException => "Cancelled or timed out.",
            ArgumentException a => StripParam(a.Message),
            FormatException f => f.Message,
            InvalidOperationException i => i.Message,
            UnauthorizedAccessException => "Permission denied accessing the wallet directory.",
            IOException io => io.Message,
            _ => $"Unexpected error ({ex.GetType().Name}).",
        };
        return SecretRedactor.Redact(msg);
    }

    private static string StripParam(string m)
    {
        var i = m.IndexOf(" (Parameter '", StringComparison.Ordinal);
        return i >= 0 ? m[..i] : m;
    }
}

/// <summary>An error whose message is written for the user.</summary>
public class UserException : Exception
{
    public UserException(string message) : base(message) { }
}
