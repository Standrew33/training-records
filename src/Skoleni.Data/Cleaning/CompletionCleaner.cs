using System.Text.RegularExpressions;
using Skoleni.Core.Models;
using Skoleni.Data.Parsing;

namespace Skoleni.Data.Cleaning;

public static partial class CompletionCleaner
{
    /// <summary>The earliest credible completion date; "1900-01-01" is a placeholder, not reality.</summary>
    public static readonly DateOnly PlausibleFrom = new(2000, 1, 1);

    public static IReadOnlyList<Completion> Clean(
        IReadOnlyList<CsvRow> rows,
        IReadOnlyList<Employee> employees,
        IReadOnlyDictionary<string, string> idAliases,
        IReadOnlyList<Course> courses,
        DateOnly exportDate,
        IssueLog log)
    {
        var byId = employees.ToDictionary(e => e.Id);
        var resolver = new CourseCodeResolver(courses.Select(c => c.Code));
        var courseByCode = courses.ToDictionary(c => c.Code);
        var seenRecordIds = new HashSet<string>();
        var seenNaturalKeys = new HashSet<(string, string, DateOnly)>();
        var result = new List<Completion>();

        foreach (var row in rows)
        {
            var id = row.Trimmed("id_zaznamu");
            var at = $"{id} (line {row.Line})";

            // Rows that are not records: an export comment, a repeated header, a total.
            if (!RecordId().IsMatch(id))
            {
                if (id != "id_zaznamu")
                    log.Info(Sources.Completions, $"line {row.Line}", $"Row is not a training record (\"{Shorten(id)}\").", "Ignored; neither the total nor the \"do not count\" comment affects the statistics.");
                continue;
            }

            var (code, changed) = resolver.Resolve(row["kod_skoleni"]);
            if (code is null)
            {
                log.Error(Sources.Completions, at, $"Unknown course code \"{row["kod_skoleni"]}\".", "Record dropped - it cannot be assigned unambiguously (we do not guess).");
                continue;
            }
            if (changed) log.Info(Sources.Completions, at, $"Course code \"{row["kod_skoleni"]}\" written non-standardly.", $"Corrected to {code}.");

            var rawEmployee = row.Trimmed("id_zamestnance");
            var employeeId = idAliases.GetValueOrDefault(rawEmployee, rawEmployee);
            if (!byId.TryGetValue(employeeId, out var employee))
            {
                log.Error(Sources.Completions, at, $"Record refers to a non-existent employee \"{rawEmployee}\".", "Dropped (orphan record).");
                continue;
            }
            if (employeeId != rawEmployee) log.Info(Sources.Completions, at, $"Record kept under the duplicate ID {rawEmployee}.", $"Moved to {employeeId}.");

            var rawDate = row.Trimmed("datum_absolvovani");
            if (!CzechDate.TryParse(rawDate, out var date, out var kind) || date < PlausibleFrom)
            {
                log.Error(Sources.Completions, at, $"Invalid or placeholder completion date \"{rawDate}\".", "Record dropped; the training is therefore treated as undocumented.");
                continue;
            }
            if (kind == CzechDate.Kind.ExcelSerial)
                log.Info(Sources.Completions, at, $"Date stored as an Excel number (\"{rawDate}\").", $"Converted to {date:yyyy-MM-dd}.");
            if (date > exportDate)
            {
                log.Warn(Sources.Completions, at, $"Completion date {date:yyyy-MM-dd} is after the export ({exportDate:yyyy-MM-dd}); note \"{row.Trimmed("poznamka")}\".", "Treated as a pre-entered record; it does not count towards validity.");
                continue;
            }

            if (!seenRecordIds.Add(id))
            {
                log.Info(Sources.Completions, at, "Repeated record ID (same content, different date format).", "Second occurrence skipped.");
                continue;
            }
            if (!seenNaturalKeys.Add((employeeId, code, date)))
            {
                log.Info(Sources.Completions, at, "The same training of the same person on the same day entered twice.", "Skipped.");
                continue;
            }

            var sourceValidUntil = CzechDate.ParseOrNull(row["platnost_do"]);
            var months = courseByCode[code].ValidityMonths;
            if (sourceValidUntil is { } sv && months is > 0 && sv != date.AddMonths(months.Value))
                log.Warn(Sources.Completions, at, $"platnost_do {sv:yyyy-MM-dd} does not match the catalogue ({months} months -> {date.AddMonths(months.Value):yyyy-MM-dd}).", "The catalogue validity is used.");

            if (employee.HireDate is { } hire && date < hire)
                log.Warn(Sources.Completions, at, $"Completed {date:yyyy-MM-dd}, before {employeeId} started ({hire:yyyy-MM-dd}).", "Kept (may come from a previous employer / another contract); verify.");
            if (employee.TerminationDate is { } end && date > end)
                log.Warn(Sources.Completions, at, $"Completed {date:yyyy-MM-dd}, after the employment of {employeeId} ended ({end:yyyy-MM-dd}).", "Kept; verify.");

            var price = Money.Parse(row["cena_kc"]);
            if (price < 0)
            {
                log.Warn(Sources.Completions, at, $"Negative price {price}.", "Price ignored.");
                price = null;
            }
            result.Add(new Completion(id, employeeId, code, date, sourceValidUntil, price));
        }
        return result;
    }

    private static string Shorten(string s) => s.Length <= 40 ? s : s[..40] + "…";

    [GeneratedRegex(@"^Z\d+$")]
    private static partial Regex RecordId();
}
