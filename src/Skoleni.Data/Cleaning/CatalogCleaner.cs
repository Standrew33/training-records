using System.Text.RegularExpressions;
using Skoleni.Core.Models;
using Skoleni.Data.Parsing;

namespace Skoleni.Data.Cleaning;

/// <summary>Maps a course code as written in the data to a code from the catalogue.</summary>
public sealed class CourseCodeResolver(IEnumerable<string> catalogCodes)
{
    private readonly string[] codes = catalogCodes.ToArray();

    /// <summary>
    /// Fixes only what is beyond doubt: whitespace, letter case, "_" or a space instead of "-", and a letter swapped
    /// for a digit (KYB3R -> KYBER) when the match is unique. "BOZP-X" would be a guess (X -> Z?), so it comes back
    /// unrecognised and is reported.
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
                log.Info(Sources.Catalog, code, "Validity of 0 months.", "Interpreted as a one-off training that never expires.");
            if (validity is null && audience.Kind != AudienceKind.Optional)
                log.Warn(Sources.Catalog, code, "Mandatory training without a stated validity.", "Treated as non-expiring - confirm with the training owner.");
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
            log.Info(Sources.Catalog, code, $"Validity written in words (\"{raw}\").", $"Converted to {converted} months.");
            return converted;
        }
        log.Error(Sources.Catalog, code, $"Unrecognised validity \"{raw}\".", "Treated as non-expiring.");
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
            log.Error(Sources.Catalog, code, $"Unrecognised \"povinne_pro\" (mandatory for): \"{raw}\".", "Treated as optional, so it is evaluated for nobody.");
            return new Audience(AudienceKind.Optional, []);
        }

        var values = new List<string>();
        foreach (var v in raw[prefix.Length..].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            // unify to the spelling that actually occurs in the employee register
            var match = known.FirstOrDefault(k => Text.Key(k) == Text.Key(v));
            if (match is null)
                log.Warn(Sources.Catalog, code, $"{(kind == AudienceKind.Department ? "Department" : "Position")} \"{v}\" from the catalogue does not occur in the employee register.", "Assigned to nobody.");
            values.Add(match ?? v);
        }
        return new Audience(kind, values);
    }
}
