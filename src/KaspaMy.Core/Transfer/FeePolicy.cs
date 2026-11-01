// Adapted from KasNetWallet Core (Transfer/FeePolicy.cs), same author, MIT License. Part of KaspaMy (MIT).
namespace KaspaMy.Core.Transfer;

/// <summary>
/// Fee sanity policy (N3). There is NO fixed-amount hard cap; instead an abnormal fee produces a warning the user must
/// acknowledge. Abnormal = effective fee rate (total fee ÷ fee mass) above RateMultiplier × reference rate, where the
/// reference is max(node normal estimate, 100 sompi/gram); or total fee above max(WarnFloorSompi, WarnPercent% of amount).
/// </summary>
public sealed class FeePolicy
{
    public double RateMultiplier { get; set; } = 10;
    public ulong WarnFloorSompi { get; set; } = 1_000_000; // 0.01 KAS
    public double WarnPercent { get; set; } = 1.0;
    /// <summary>R3-5: fee ≥ this fraction of the sent amount is "Abnormally high" (default 0.5 = fee is half the amount or more).</summary>
    public double AbnormalFeeToAmountRatio { get; set; } = 0.5;

    public static FeePolicy Default => new();

    public ulong AbsoluteWarnThreshold(ulong amountSompi) =>
        Math.Max(WarnFloorSompi, (ulong)Math.Min(ulong.MaxValue / 4, amountSompi * WarnPercent / 100.0));
}
