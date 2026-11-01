// Adapted from KasNetWallet Core (Transfer/TransferPlan.cs), same author, MIT License. Part of KaspaMy (MIT).
using NKaspa;

namespace KaspaMy.Core.Transfer;

/// <summary>A built and signed transfer awaiting explicit user confirmation. Contains no private key.</summary>
public sealed class TransferPlan
{
    internal TransferPlan(Transaction tx, WalletNetwork network, string from, string to, ulong amountSompi, ulong feeSompi,
        ulong changeSompi, ulong inputSompi, int inputCount, ulong mass, ulong foldedChangeSompi, IReadOnlyList<string> warnings,
        bool isSendMax, int addedInputs, int availableCoins, ulong expectedFeeSompi)
    {
        Transaction = tx; Network = network; From = from; To = to; AmountSompi = amountSompi; FeeSompi = feeSompi;
        ChangeSompi = changeSompi; InputSompi = inputSompi; InputCount = inputCount; Mass = mass;
        FoldedChangeSompi = foldedChangeSompi; Warnings = warnings; IsSendMax = isSendMax; AddedInputsForChange = addedInputs;
        AvailableCoins = availableCoins; ExpectedFeeSompi = expectedFeeSompi;
        TransactionId = tx.GetId().ToString();
    }

    public Transaction Transaction { get; }
    public WalletNetwork Network { get; }
    public string From { get; }
    public string To { get; }
    public ulong AmountSompi { get; }
    public ulong FeeSompi { get; }
    public ulong ChangeSompi { get; }
    public ulong InputSompi { get; }
    public int InputCount { get; }
    public ulong Mass { get; }
    public string TransactionId { get; }
    public bool Broadcast { get; internal set; }

    /// <summary>Dust change paid as fee after trying extra inputs (requires acknowledgement).</summary>
    public ulong FoldedChangeSompi { get; }
    /// <summary>Fee NKaspa should charge for this tx at the chosen rate, without folding.</summary>
    public ulong ExpectedFeeSompi { get; }
    public bool IsSendMax { get; }
    public int AddedInputsForChange { get; }
    public int AvailableCoins { get; }
    /// <summary>Must be acknowledged by the user (second confirmation) before broadcast.</summary>
    public IReadOnlyList<string> Warnings { get; }

    public string Summary() =>
        $"Network: {Network}\nFrom: {From}\nTo: {To}\nAmount: {Kas.Format(AmountSompi)} KAS" + (IsSendMax ? " (Send max — no change)" : "") +
        $"\nFee: {Kas.Format(FeeSompi)} KAS\nChange: {Kas.Format(ChangeSompi)} KAS\nInputs: {InputCount} of {AvailableCoins} UTXO ({Kas.Format(InputSompi)} KAS)" +
        (AddedInputsForChange > 0 ? $"\n(added {AddedInputsForChange} extra input so the change is a valid output instead of being paid as fee)" : "") +
        $"\nMass: {Mass}\nTx id: {TransactionId}" +
        string.Concat(Warnings.Select(w => "\n\nWARNING: " + w));
}

public static class Kas
{
    public const ulong SompiPerKas = 100_000_000;

    public static string Format(ulong sompi) =>
        (sompi / SompiPerKas).ToString(System.Globalization.CultureInfo.InvariantCulture) + "." +
        (sompi % SompiPerKas).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Parses a decimal KAS amount (invariant culture, max 8 decimals) to sompi.</summary>
    public static ulong ParseKas(string text)
    {
        if (!decimal.TryParse(text?.Trim(), System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var kas))
            throw new FormatException("Amount is not a valid number.");
        if (kas <= 0) throw new FormatException("Amount must be positive.");
        decimal sompi;
        try { sompi = kas * SompiPerKas; } catch (OverflowException) { throw new FormatException("Amount too large."); }
        if (sompi != decimal.Truncate(sompi)) throw new FormatException("Amount has more than 8 decimals.");
        if (sompi > ulong.MaxValue) throw new FormatException("Amount too large.");
        return (ulong)sompi;
    }
}
