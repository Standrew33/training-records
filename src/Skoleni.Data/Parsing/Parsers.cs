using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Skoleni.Data.Parsing;

public static partial class CzechDate
{
    private static readonly string[] Formats = ["yyyy-MM-dd", "d. M. yyyy", "d.M.yyyy", "dd.MM.yyyy"];

    public enum Kind { Iso, Czech, ExcelSerial }

    /// <summary>
    /// Supports 2019-01-06, "6. 1. 2019", 06.01.2019 and Excel serial numbers (43552).
    /// Impossible dates (31. 9.) and empty values are reported as a failure - they are never "fixed" to the nearest valid date.
    /// </summary>
    public static bool TryParse(string? raw, out DateOnly date, out Kind kind)
    {
        date = default;
        kind = Kind.Iso;
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return false;

        if (ExcelSerial().IsMatch(s))
        {
            kind = Kind.ExcelSerial;
            var serial = int.Parse(s, CultureInfo.InvariantCulture);
            // Excel treats 1900 as a leap year; for dates after 1 Mar 1900 the epoch is 30 Dec 1899.
            if (serial < 61) return false;
            date = new DateOnly(1899, 12, 30).AddDays(serial);
            return true;
        }
        if (!DateOnly.TryParseExact(s, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return false;
        kind = s.Length == 10 && s[4] == '-' ? Kind.Iso : Kind.Czech;
        return true;
    }

    public static DateOnly? ParseOrNull(string? raw) => TryParse(raw, out var d, out _) ? d : null;

    [GeneratedRegex(@"^\d{5}$")]
    private static partial Regex ExcelSerial();
}

public static class Money
{
    /// <summary>"1 500,00", "2 100 Kč", "380,- Kč", "zdarma" (free) -> number; empty or unrecognisable -> null.</summary>
    public static decimal? Parse(string? raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0) return null;
        if (s == "zdarma") return 0m;
        s = s.Replace("kč", "").Replace(",-", "").Replace('\u00a0', ' ').Replace(" ", "").Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}

public static class Text
{
    /// <summary>Key for comparing spellings: no diacritics, lower case, single spaces.</summary>
    public static string Key(string? s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return Regex.Replace(sb.ToString().ToLowerInvariant(), @"\s+", " ").Trim();
    }

    /// <summary>"  LUCIE" -> "Lucie", "růžička" -> "Růžička", "Nováková-Malá" stays "Nováková-Malá".</summary>
    public static string TitleCase(string? s)
    {
        var chars = (s ?? "").Trim().ToLowerInvariant().ToCharArray();
        var startOfWord = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (startOfWord && char.IsLetter(chars[i])) chars[i] = char.ToUpperInvariant(chars[i]);
            startOfWord = chars[i] is ' ' or '-';
        }
        return new string(chars);
    }

    public static int EditDistance(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return dp[a.Length, b.Length];
    }
}
