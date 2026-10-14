using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Skoleni.Data.Parsing;

public static partial class CzechDate
{
    private static readonly string[] Formats = ["yyyy-MM-dd", "d. M. yyyy", "d.M.yyyy", "dd.MM.yyyy"];

    public enum Kind { Iso, Czech, ExcelSerial }

    /// <summary>
    /// Podporuje 2019-01-06, „6. 1. 2019“, 06.01.2019 a excelová sériová čísla (43552).
    /// Nemožná data (31. 9.) a prázdné hodnoty vrací jako neúspěch – nikdy je „neopravuje“ na nejbližší platné.
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
            // Excel počítá 1900 jako přestupný; pro data po 1. 3. 1900 platí epocha 30. 12. 1899.
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
    /// <summary>„1 500,00“, „2 100 Kč“, „380,- Kč“, „zdarma“ → číslo; prázdné nebo nerozpoznatelné → null.</summary>
    public static decimal? Parse(string? raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0) return null;
        if (s == "zdarma") return 0m;
        s = s.Replace("kč", "").Replace(",-", "").Replace(' ', ' ').Replace(" ", "").Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}

public static class Text
{
    /// <summary>Klíč pro porovnání zápisů: bez diakritiky, malá písmena, jedna mezera.</summary>
    public static string Key(string? s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return Regex.Replace(sb.ToString().ToLowerInvariant(), @"\s+", " ").Trim();
    }

    /// <summary>„  LUCIE“ → „Lucie“, „růžička“ → „Růžička“, „Nováková-Malá“ → „Nováková-Malá“.</summary>
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
