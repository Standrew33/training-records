using System.Text.RegularExpressions;
using Skoleni.Core.Models;
using Skoleni.Data.Parsing;

namespace Skoleni.Data.Cleaning;

public static partial class CompletionCleaner
{
    /// <summary>Nejstarší věrohodné datum absolvování; „1900-01-01“ je zástupná hodnota, ne skutečnost.</summary>
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
            var at = $"{id} (řádek {row.Line})";

            // Řádky, které nejsou záznamem: komentář z exportu, opakovaná hlavička, součet.
            if (!RecordId().IsMatch(id))
            {
                if (id != "id_zaznamu")
                    log.Info(Sources.Completions, $"řádek {row.Line}", $"Řádek není záznam školení („{Shorten(id)}“).", "Ignorován; součet ani komentář „nezapočítávat“ se do statistik nepromítají.");
                continue;
            }

            var (code, changed) = resolver.Resolve(row["kod_skoleni"]);
            if (code is null)
            {
                log.Error(Sources.Completions, at, $"Neznámý kód školení „{row["kod_skoleni"]}“.", "Záznam vyřazen – nelze jednoznačně přiřadit (nehádáme).");
                continue;
            }
            if (changed) log.Info(Sources.Completions, at, $"Kód „{row["kod_skoleni"]}“ zapsán nestandardně.", $"Opraveno na {code}.");

            var rawEmployee = row.Trimmed("id_zamestnance");
            var employeeId = idAliases.GetValueOrDefault(rawEmployee, rawEmployee);
            if (!byId.TryGetValue(employeeId, out var employee))
            {
                log.Error(Sources.Completions, at, $"Záznam odkazuje na neexistujícího zaměstnance „{rawEmployee}“.", "Vyřazen (osiřelý záznam).");
                continue;
            }
            if (employeeId != rawEmployee) log.Info(Sources.Completions, at, $"Záznam veden na duplicitní ID {rawEmployee}.", $"Přesunut na {employeeId}.");

            var rawDate = row.Trimmed("datum_absolvovani");
            if (!CzechDate.TryParse(rawDate, out var date, out var kind) || date < PlausibleFrom)
            {
                log.Error(Sources.Completions, at, $"Neplatné nebo zástupné datum absolvování „{rawDate}“.", "Záznam vyřazen; školení se tím pádem bere jako nedoložené.");
                continue;
            }
            if (kind == CzechDate.Kind.ExcelSerial)
                log.Info(Sources.Completions, at, $"Datum uložené jako číslo z Excelu („{rawDate}“).", $"Převedeno na {date:yyyy-MM-dd}.");
            if (date > exportDate)
            {
                log.Warn(Sources.Completions, at, $"Datum absolvování {date:yyyy-MM-dd} je po exportu ({exportDate:yyyy-MM-dd}); poznámka „{row.Trimmed("poznamka")}“.", "Bráno jako předběžný zápis, do platnosti se nezapočítává.");
                continue;
            }

            if (!seenRecordIds.Add(id))
            {
                log.Info(Sources.Completions, at, "Opakované ID záznamu (stejný obsah, jiný formát data).", "Druhý výskyt vynechán.");
                continue;
            }
            if (!seenNaturalKeys.Add((employeeId, code, date)))
            {
                log.Info(Sources.Completions, at, "Totéž školení téhož člověka ve stejný den zapsané dvakrát.", "Vynecháno.");
                continue;
            }

            var sourceValidUntil = CzechDate.ParseOrNull(row["platnost_do"]);
            var months = courseByCode[code].ValidityMonths;
            if (sourceValidUntil is { } sv && months is > 0 && sv != date.AddMonths(months.Value))
                log.Warn(Sources.Completions, at, $"platnost_do {sv:yyyy-MM-dd} nesedí s katalogem ({months} měs. → {date.AddMonths(months.Value):yyyy-MM-dd}).", "Použita platnost z katalogu.");

            if (employee.HireDate is { } hire && date < hire)
                log.Warn(Sources.Completions, at, $"Absolvováno {date:yyyy-MM-dd}, před nástupem {employeeId} ({hire:yyyy-MM-dd}).", "Ponecháno (může jít o předchozí zaměstnavatele / jiný poměr); ověřit.");
            if (employee.TerminationDate is { } end && date > end)
                log.Warn(Sources.Completions, at, $"Absolvováno {date:yyyy-MM-dd}, po ukončení poměru {employeeId} ({end:yyyy-MM-dd}).", "Ponecháno; ověřit.");

            var price = Money.Parse(row["cena_kc"]);
            if (price < 0)
            {
                log.Warn(Sources.Completions, at, $"Záporná cena {price}.", "Cena ignorována.");
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
