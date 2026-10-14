using System.Globalization;
using Skoleni.Core.Models;
using Skoleni.Data.Parsing;

namespace Skoleni.Data.Cleaning;

public static class TermCleaner
{
    public static IReadOnlyList<Term> Clean(IReadOnlyList<CsvRow> rows, IReadOnlyList<Course> courses, IssueLog log)
    {
        var resolver = new CourseCodeResolver(courses.Select(c => c.Code));
        var seen = new Dictionary<string, string>();
        var drafts = new List<Term>();

        foreach (var row in rows)
        {
            var id = row.Trimmed("id_terminu");
            var (code, changed) = resolver.Resolve(row["kod_skoleni"]);
            if (changed && code is not null) log.Warn(Sources.Terms, id, $"Course code \"{row["kod_skoleni"]}\" in the term.", $"Corrected to {code}.");

            var rawDate = row.Trimmed("datum");
            DateOnly? date = CzechDate.TryParse(rawDate, out var d, out _) ? d : null;
            var capacity = int.TryParse(row.Trimmed("kapacita"), out var cap) ? cap : 0;
            var booked = int.TryParse(row.Trimmed("obsazeno"), out var b) ? b : 0;
            var rawPrice = row.Trimmed("cena_na_osobu");
            var price = Money.Parse(rawPrice);
            var (start, end) = ParseTimeRange(row.Trimmed("cas"));
            var place = row.Trimmed("misto");

            string? excluded = null;
            if (code is null) excluded = $"unknown course code \"{row["kod_skoleni"]}\"";
            else if (date is null) excluded = $"invalid date \"{rawDate}\"";
            else if (Text.Key(row["stav"]).StartsWith("zrus")) excluded = "cancelled";
            else if (capacity <= 0) excluded = "zero capacity";

            if (date is null) log.Error(Sources.Terms, id, $"Invalid term date \"{rawDate}\".", "Term excluded from planning.");
            if (capacity <= 0) log.Error(Sources.Terms, id, "Capacity is 0.", "Term excluded from planning.");
            if (booked > capacity && capacity > 0)
                log.Warn(Sources.Terms, id, $"Overbooked: {booked} booked of {capacity}.", "Free capacity = 0; check whether it is a typo.");
            if (price is null) log.Warn(Sources.Terms, id, $"Price not stated (\"{rawPrice}\").", "The median price of the same course is used for the cost estimate.");
            else if (!rawPrice.All(char.IsDigit)) log.Info(Sources.Terms, id, $"Price written as \"{rawPrice}\".", $"Converted to {price.Value.ToString(CultureInfo.InvariantCulture)}.");
            if (start is null && date is not null) log.Warn(Sources.Terms, id, $"Time \"{row["cas"]}\" cannot be read.", "For collision checks it is assumed to take the whole day.");

            if (excluded is null && code is not null && date is not null)
            {
                var key = $"{code}|{date:o}|{row.Trimmed("cas")}|{place}";
                if (seen.TryGetValue(key, out var original))
                {
                    excluded = $"duplicate of term {original}";
                    log.Error(Sources.Terms, id, $"Duplicate of term {original} (same course, day, time and place).", "Excluded so the capacity is not counted twice.");
                }
                else seen[key] = id;
            }

            var weekend = date is { DayOfWeek: DayOfWeek.Saturday or DayOfWeek.Sunday };
            if (weekend) log.Warn(Sources.Terms, id, $"Term {date:yyyy-MM-dd} falls on a weekend.", "Kept and flagged in the plan; confirm with the trainer.");

            drafts.Add(new Term(id, code ?? row.Trimmed("kod_skoleni"), date, start, end, place, capacity, booked, price, false, excluded, weekend));
        }

        // Missing price: median price of the usable terms of the same course, always marked as an estimate.
        var medians = drafts.Where(t => t.Price is not null && t.ExcludedReason is null)
            .GroupBy(t => t.CourseCode)
            .ToDictionary(g => g.Key, g => Median(g.Select(t => t.Price!.Value)));
        return drafts
            .Select(t => t.Price is null && medians.TryGetValue(t.CourseCode, out var m) ? t with { Price = m, PriceEstimated = true } : t)
            .OrderBy(t => t.Date ?? DateOnly.MaxValue).ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static (TimeOnly?, TimeOnly?) ParseTimeRange(string raw)
    {
        var parts = raw.Split('-', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 && TimeOnly.TryParse(parts[0], CultureInfo.InvariantCulture, out var s) && TimeOnly.TryParse(parts[1], CultureInfo.InvariantCulture, out var e) && s < e
            ? (s, e)
            : (null, null);
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToList();
        return sorted[sorted.Count / 2];
    }
}
