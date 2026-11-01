// Adapted from KasNetWallet Core (MnemonicService.cs), same author, MIT License. Part of KaspaMy (MIT).
using NKaspa;

namespace KaspaMy.Core;

/// <summary>BIP39 helpers on top of NKaspa. Never log the returned phrase.</summary>
public static class MnemonicService
{
    public static string Generate(int words = 24)
    {
        var count = words switch { 12 => WordCount.Twelve, 24 => WordCount.TwentyFour, _ => throw new ArgumentException("Only 12 or 24 words are supported.", nameof(words)) };
        // NKaspa 5c49251: ToPhrase() is the explicit raw export; never call Mnemonic.ToString() (NL-072).
        var m = new Mnemonic(Wordlist.English, count);
        return m.ToPhrase();
    }

    /// <summary>Normalizes whitespace/case and validates the checksum. Throws FormatException on invalid input.</summary>
    public static string Normalize(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) throw new FormatException("Mnemonic is empty.");
        // F5: zero-width / invisible format characters (Unicode Cf: U+200B/C/D, U+FEFF BOM, U+00AD soft hyphen, ...) are
        // REJECTED, not stripped, so the phrase the user confirms is exactly the phrase that is used. NKaspa's Mnemonic.Parse
        // strips them instead (library-level leniency); see docs/FOR-TESTERS.md.
        for (var i = 0; i < phrase.Length; i++)
            if (char.GetUnicodeCategory(phrase[i]) == System.Globalization.UnicodeCategory.Format)
                throw new FormatException($"Word #{phrase[..i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length + (i > 0 && !char.IsWhiteSpace(phrase[i - 1]) ? 0 : 1)} of the recovery phrase contains an invisible character (U+{(int)phrase[i]:X4}, e.g. a zero-width space or BOM), " +
                    "usually from copy-paste. Retype the words by hand or paste them into a plain-text editor first; invisible characters are not removed automatically.");
        var words = phrase.Normalize(System.Text.NormalizationForm.FormKC).Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is not (12 or 15 or 18 or 21 or 24)) throw new FormatException($"Mnemonic must have 12, 15, 18, 21 or 24 words (got {words.Length}).");
        // Zero-width chars / BOM are not whitespace: they stay inside a word and are rejected below (KW-016).
        var wl = Wordlist.English;
        for (var i = 0; i < words.Length; i++)
            if (!wl.TryGetIndex(words[i], out _)) throw new FormatException($"Word #{i + 1} is not in the BIP39 English list.");
        var joined = string.Join(' ', words);
        // NKaspa 5c49251 (R1 resolved): Mnemonic.Parse enforces the checksum. Our own word checks above stay as defense in depth
        // and still REJECT zero-width characters (NKaspa would strip them silently; KW-016).
        try { _ = Mnemonic.Parse(joined, Wordlist.English); }
        catch (FormatException ex) when (ex.Message.Contains("checksum", StringComparison.OrdinalIgnoreCase))
        { throw new FormatException("Invalid mnemonic checksum: one or more words are wrong or out of order."); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { throw new FormatException("Invalid mnemonic (unknown word)."); }
        return joined;
    }

    public static SecretText GenerateSecret(int words = 24) => new(Generate(words));

    public static bool IsValid(string phrase)
    {
        try { Normalize(phrase); return true; } catch (FormatException) { return false; }
    }
}
