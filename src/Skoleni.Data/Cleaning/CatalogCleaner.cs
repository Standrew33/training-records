using System.Text.RegularExpressions;
using Skoleni.Core.Models;
using Skoleni.Data.Parsing;

namespace Skoleni.Data.Cleaning;

/// <summary>Přiřadí zapsaný kód školení ke kódu z katalogu.</summary>
public sealed class CourseCodeResolver(IEnumerable<string> catalogCodes)
{
    private readonly string[] codes = catalogCodes.ToArray();

    /// <summary>
    /// Opravuje jen to, co je nepochybné: mezery, velikost písmen, „_“ nebo mezera místo „-“,
    /// a záměnu písmene za číslici (KYB3R → KYBER), pokud je shoda jednoznačná.
    /// „BOZP-X“ by byl odhad (X → Z?), takže se vrací jako nerozpoznané a hlásí se.
    /// </summary>
    public (string? Code, bool Changed) Resolve(string? raw)
    {
        var original = raw ?? "";
        var normalized = Regex.Replace(original.Trim().ToUpperInvariant(), @"[_\s]+", "-");
        if (codes.Contains(normalized)) return (normalized, normalized != original);

        if (normalized.Any(char.IsDigit))
        {
            var near = codes.Where(k => !k.Any(char.IsDigit) && Text.EditDistance(k, normalized) <= 1).ToList();
            if (near.Count == 1) return (near[0], true);
        }
        return (null, false);
    }
}

public static class CatalogCleaner
{
    public static IReadOnlyList<Course> Clean(IReadOnlyList<CsvRow> rows, IReadOnlyCollection<string> knownDepartments, IReadOnlyCollection<string> knownPositions, IssueLog log)
    {
        var courses = new List<Course>();
        foreach (var row in rows)
        {
            var code = row.Trimmed("kod");
            if (code.Length == 0) continue;

            var validity = ParseValidity(row.Trimmed("platnost_mesicu"), code, log);
            var audience = ParseAudience(row.Trimmed("povinne_pro"), code, knownDepartments, knownPositions, log);
            if (validity == 0)
                log.Info(Sources.Catalog, code, "Platnost 0 měsíců.", "Chápáno jako jednorázové školení bez expirace.");
            if (validity is null && audience.Kind != AudienceKind.Optional)
                log.Warn(Sources.Catalog, code, "Povinné školení bez uvedené platnosti.", "Počítá se jako bez expirace – ověřit s garantem školení.");
            courses.Add(new Course(code, row.Trimmed("nazev"), validity, audience));
        }
        return courses;
    }

    private static int? ParseValidity(string raw, string code, IssueLog log)
    {
        if (raw.Length == 0) return null;
        if (int.TryParse(raw, out var months) && months >= 0) return months;
        var m = Regex.Match(raw.ToLowerInvariant(), @"^(\d+)\s*(rok|roky|let)$");
        if (m.Success)
        {
            var converted = int.Parse(m.Groups[1].Value) * 12;
            log.Info(Sources.Catalog, code, $"Platnost zapsána slovy („{raw}“).", $"Převedeno na {converted} měsíců.");
            return converted;
        }
        log.Error(Sources.Catalog, code, $"Nerozpoznaná platnost „{raw}“.", "Školení se bere jako bez expirace.");
        return null;
    }

    private static Audience ParseAudience(string raw, string code, IReadOnlyCollection<string> departments, IReadOnlyCollection<string> positions, IssueLog log)
    {
        if (raw == "VSICHNI") return new Audience(AudienceKind.Everyone, []);
        if (raw == "NEPOVINNE") return new Audience(AudienceKind.Optional, []);

        var (kind, known, prefix) = raw.StartsWith("UTVAR:") ? (AudienceKind.Department, departments, "UTVAR:")
            : raw.StartsWith("POZICE:") ? (AudienceKind.Position, positions, "POZICE:")
            : (AudienceKind.Optional, [], "");
        if (prefix.Length == 0)
        {
            log.Error(Sources.Catalog, code, $"Nerozpoznané „povinne_pro“: „{raw}“.", "Školení je bráno jako nepovinné, nikomu se nevyhodnocuje.");
            return new Audience(AudienceKind.Optional, []);
        }

        var values = new List<string>();
        foreach (var v in raw[prefix.Length..].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            // sjednotit na zápis, který se v evidenci skutečně vyskytuje
            var match = known.FirstOrDefault(k => Text.Key(k) == Text.Key(v));
            if (match is null)
                log.Warn(Sources.Catalog, code, $"{(kind == AudienceKind.Department ? "Útvar" : "Pozice")} „{v}“ z katalogu se v evidenci zaměstnanců nevyskytuje.", "Nikomu se nepřiřadí.");
            values.Add(match ?? v);
        }
        return new Audience(kind, values);
    }
}
