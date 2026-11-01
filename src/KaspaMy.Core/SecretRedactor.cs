// Adapted from KasNetWallet Core (SecretRedactor.cs), same author, MIT License. Part of KaspaMy (MIT).
using System.Text.RegularExpressions;
using NKaspa;

namespace KaspaMy.Core;

/// <summary>
/// Last-line defense for logs and error text: masks extended keys (xprv/kprv/ktrv/tprv…), hex secrets, base64/base64url
/// key-sized values, and mnemonic word runs (spaces, wide gaps, list/HTML/markdown markup, "and"/"then" joiners).
/// R3-8: a bare 64-hex value (tx id, block hash) stays visible; it is masked only next to a secret label
/// ("private key", "seed", "secret", "sk", "prv"…). Hex runs ≥ 128 chars are always masked.
/// </summary>
public static partial class SecretRedactor
{
    public const string Mask = "[REDACTED]";
    private static readonly HashSet<string> Words = LoadWords();

    [GeneratedRegex(@"\b[a-z]{1,2}(prv|tprv|trv)[1-9A-HJ-NP-Za-km-z]{50,}", RegexOptions.CultureInvariant)]
    private static partial Regex ExtPriv();

    [GeneratedRegex(@"\b(xprv|tprv|kprv|ktrv)[1-9A-HJ-NP-Za-km-z]{20,}", RegexOptions.CultureInvariant)]
    private static partial Regex ExtPrivNamed();

    [GeneratedRegex(@"(?<![0-9a-fA-F])[0-9a-fA-F]{64,}(?![0-9a-fA-F])", RegexOptions.CultureInvariant)]
    private static partial Regex LongHex();

    [GeneratedRegex(@"(priv|private|secret|seed|mnemonic|entropy|\bsk|\bkey\b|prv|signing)[^\n]{0,24}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SecretLabelBefore();

    private static string RedactHex(string s) => LongHex().Replace(s, m =>
    {
        if (m.Length >= 128) return Mask;
        var before = s.Substring(Math.Max(0, m.Index - 32), Math.Min(32, m.Index));
        return SecretLabelBefore().IsMatch(before) ? Mask : m.Value;
    });

    [GeneratedRegex(@"<[^<>\n]{0,40}>|&nbsp;|\b(?:and|then|or|plus|followed by)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex GapNoise();

    [GeneratedRegex(@"[A-Za-z]+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var s = ExtPrivNamed().Replace(text, Mask);
        s = ExtPriv().Replace(s, Mask);
        s = RedactHex(s);
        s = RedactBase64(s);
        return RedactMnemonicRuns(s);
    }

    private static string RedactMnemonicRuns(string s)
    {
        var matches = Word().Matches(s).Where(m => !Connectors.Contains(m.Value.ToLowerInvariant()) && !InsideTag(s, m)).ToList();
        var result = new System.Text.StringBuilder(s);
        int runStart = -1, runCount = 0; var structured = false;
        var spans = new List<(int start, int end)>();
        for (var i = 0; i <= matches.Count; i++)
        {
            var isWord = i < matches.Count && Words.Contains(matches[i].Value.ToLowerInvariant());
            if (isWord && runCount > 0)
            {
                // separators: whitespace, or list/JSON/numbering punctuation and digits ("1. a, 2. b", ["a","b"], a-b)
                var prev = matches[i - 1];
                var gap = s.AsSpan(prev.Index + prev.Length, matches[i].Index - prev.Index - prev.Length);
                if (!IsSeparator(gap, out var noisy)) { Flush(i); runStart = i; runCount = 1; structured = false; continue; }
                if (noisy || !gap.IsWhiteSpace()) structured = true;
            }
            if (isWord) { if (runCount == 0) runStart = i; runCount++; }
            else Flush(i);
        }
        for (var k = spans.Count - 1; k >= 0; k--)
            result.Remove(spans[k].start, spans[k].end - spans[k].start).Insert(spans[k].start, Mask);
        return result.ToString();

        void Flush(int i)
        {
            if (runCount >= (structured ? StructuredThreshold : 12))
            {
                var first = matches[runStart]; var last = matches[runStart + runCount - 1];
                spans.Add((first.Index, last.Index + last.Length));
            }
            runCount = 0; runStart = -1; structured = false;
        }
    }

    /// <summary>Runs using list punctuation/numbering are masked from this many words (KDW-10).</summary>
    public const int StructuredThreshold = 6;
    public const int MaxGap = 48;

    private static readonly HashSet<string> Connectors = new(StringComparer.Ordinal) { "and", "then", "or", "plus", "followed", "by", "nbsp" };

    private static bool InsideTag(string s, Match m)
    {
        var lt = s.LastIndexOf('<', m.Index); var gt = s.LastIndexOf('>', m.Index);
        return lt >= 0 && lt > gt && m.Index - lt <= 40;
    }

    /// <summary>Whitespace-only gaps of any length (≤ 400) separate words; otherwise ≤ MaxGap separator chars after removing HTML tags/joiners.</summary>
    private static bool IsSeparator(ReadOnlySpan<char> gapSpan, out bool noisy)
    {
        noisy = false;
        if (gapSpan.Length == 0 || gapSpan.Length > 400) return false;
        if (gapSpan.IsWhiteSpace()) return true;
        var gap = gapSpan.ToString();
        var cleaned = GapNoise().Replace(gap, " ");
        noisy = cleaned != gap;
        var trimmed = cleaned.Trim();
        if (trimmed.Length > MaxGap) return false;
        foreach (var c in cleaned)
            if (!(char.IsWhiteSpace(c) || char.IsDigit(c) || char.IsControl(c) || ",.-_;:|/\\\"'`[](){}<>*#~+=•·–—>&".Contains(c))) return false;
        return true;
    }

    // standard base64 and base64url ('-', '_') alphabets.
    [GeneratedRegex(@"(?<![A-Za-z0-9+/_\-])[A-Za-z0-9+/_\-]{43,}={0,2}(?![A-Za-z0-9+/_\-=])", RegexOptions.CultureInvariant)]
    private static partial Regex Base64Run();

    /// <summary>
    /// R3-8: masks base64/base64url runs whose length matches a 32- or 64-byte secret (43/44, 86–88 chars) — any case, as long as
    /// it contains a digit or mixed case — and any mixed-case run ≥ 120 chars. Other lengths (request ids, slugs) stay visible.
    /// Lower-case bech32 address payloads never contain '-', '_', '+' or '/' and are preceded by "kaspa:" so they are skipped.
    /// </summary>
    private static string RedactBase64(string s) => Base64Run().Replace(s, m =>
    {
        var v = m.Value.TrimEnd('=');
        var pre = s.Substring(Math.Max(0, m.Index - 10), Math.Min(10, m.Index));
        if (pre.EndsWith("kaspa:", StringComparison.OrdinalIgnoreCase) || pre.EndsWith("kaspatest:", StringComparison.OrdinalIgnoreCase)) return m.Value;
        var keySized = v.Length is 43 or 44 or 86 or 87 or 88;
        var mixed = v.Any(char.IsUpper) && v.Any(char.IsLower);
        var hasDigit = v.Any(char.IsDigit);
        if (keySized && (mixed || hasDigit)) return Mask;
        if (v.Length >= 120 && mixed) return Mask;
        return m.Value;
    });

    private static HashSet<string> LoadWords()
    {
        var wl = Wordlist.English;
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < wl.Count; i++) set.Add(wl[i]);
        return set;
    }
}
