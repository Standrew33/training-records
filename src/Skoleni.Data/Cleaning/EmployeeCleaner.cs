using System.Text.RegularExpressions;
using Skoleni.Core.Models;
using Skoleni.Data.Parsing;

namespace Skoleni.Data.Cleaning;

public sealed record EmployeeCleaningResult(IReadOnlyList<Employee> Employees, IReadOnlyDictionary<string, string> IdAliases);

public static partial class EmployeeCleaner
{
    public static EmployeeCleaningResult Clean(IReadOnlyList<CsvRow> rows, DateOnly exportDate, IssueLog log)
    {
        var aliases = FindDuplicates(rows, log);
        var departments = BuildDepartmentMap(rows);

        var drafts = new List<Draft>();
        var seenIds = new HashSet<string>();
        foreach (var row in rows)
        {
            var id = row.Trimmed("id_zamestnance");
            if (aliases.ContainsKey(id)) continue;
            if (!seenIds.Add(id))
            {
                log.Error(Sources.Employees, id, $"ID zaměstnance se v souboru opakuje (řádek {row.Line}).", "Druhý výskyt vynechán.");
                continue;
            }
            drafts.Add(ToDraft(row, id, departments, exportDate, log));
        }

        var byEmail = drafts.Where(d => d.Email.Length > 0).GroupBy(d => d.Email).ToDictionary(g => g.Key, g => g.First().Id);
        foreach (var d in drafts.Where(d => d.ManagerEmail.Length > 0))
        {
            if (byEmail.TryGetValue(d.ManagerEmail, out var managerId) && managerId != d.Id) d.ManagerId = managerId;
            else log.Warn(Sources.Employees, d.Id, $"Nadřízený „{d.ManagerEmail}“ v evidenci neexistuje.", "Zaměstnanec zůstane bez nadřízeného (je vidět jen v celkových přehledech).");
        }
        InferMissingDepartments(drafts, log);

        var employees = drafts.Select(d => new Employee(d.Id, d.FirstName, d.LastName, d.Email, d.Department, d.DepartmentInferred, d.Position, d.ManagerId, d.HireDate, d.TerminationDate, d.State)).ToList();
        return new EmployeeCleaningResult(employees, aliases);
    }

    /// <summary>
    /// Duplicitní osoba = shodné rodné číslo pod různým ID (v datech: překlepy ve jménu, e-mail s „2“).
    /// Rodné číslo se čte jen tady, jen pro tuto shodu, a nikam se neukládá. Zachová se nejnižší ID.
    /// </summary>
    private static Dictionary<string, string> FindDuplicates(IReadOnlyList<CsvRow> rows, IssueLog log)
    {
        var aliases = new Dictionary<string, string>();
        foreach (var group in rows.Where(r => r.Trimmed("rodne_cislo").Length > 0).GroupBy(r => r.Trimmed("rodne_cislo")))
        {
            var ids = group.Select(r => r.Trimmed("id_zamestnance")).Distinct().OrderBy(i => i, StringComparer.Ordinal).ToList();
            if (ids.Count < 2) continue;
            foreach (var dup in ids.Skip(1))
            {
                aliases[dup] = ids[0];
                log.Error(Sources.Employees, dup, $"Duplicitní osoba: shodné identifikační číslo osoby jako {ids[0]} (jiné ID, jméno s překlepem).", $"Sloučeno do {ids[0]}; záznamy školení se přesměrují.");
            }
        }
        return aliases;
    }

    /// <summary>Normalizovaný klíč → nejčastější zápis. Zkrácený název („Studijní“) se přiřadí jedinému delšímu, který jím začíná.</summary>
    private static Dictionary<string, string> BuildDepartmentMap(IReadOnlyList<CsvRow> rows)
    {
        var map = rows.Select(r => r.Trimmed("utvar")).Where(s => s.Length > 0)
            .GroupBy(Text.Key)
            .ToDictionary(g => g.Key, g => g.GroupBy(x => x).OrderByDescending(v => v.Count()).First().Key);
        foreach (var key in map.Keys.ToList())
        {
            var longer = map.Keys.Where(o => o != key && o.StartsWith(key + " ")).ToList();
            if (longer.Count == 1) map[key] = map[longer[0]];
        }
        return map;
    }

    private static Draft ToDraft(CsvRow row, string id, Dictionary<string, string> departments, DateOnly exportDate, IssueLog log)
    {
        var rawEmail = row.Trimmed("email");
        var email = Regex.Replace(rawEmail, @"\s+", "").ToLowerInvariant();
        if (!EmailPattern().IsMatch(email))
            log.Warn(Sources.Employees, id, $"Neplatný e-mail „{rawEmail}“.", "Ponechán beze změny, k párování se nepoužívá.");
        else if (email != rawEmail)
            log.Info(Sources.Employees, id, $"E-mail „{rawEmail}“ obsahuje mezeru.", "Mezery odstraněny.");

        var first = Text.TitleCase(row["jmeno"]);
        var last = Text.TitleCase(row["prijmeni"]);
        if (first != row["jmeno"] || last != row["prijmeni"])
            log.Info(Sources.Employees, id, $"Jméno „{row["jmeno"]}“ / „{row["prijmeni"]}“ s nekonzistentní velikostí písmen nebo mezerami.", "Normalizováno.");

        var hireRaw = row.Trimmed("datum_nastupu");
        DateOnly? hire = null;
        if (CzechDate.TryParse(hireRaw, out var h, out var kind))
        {
            hire = h;
            if (kind != CzechDate.Kind.Iso) log.Info(Sources.Employees, id, $"Datum nástupu v jiném formátu („{hireRaw}“).", $"Převedeno na {h:yyyy-MM-dd}.");
        }
        else log.Warn(Sources.Employees, id, $"Datum nástupu „{hireRaw}“ nelze přečíst.", "Nástup neznámý.");
        var end = CzechDate.ParseOrNull(row["datum_ukonceni"]);

        var stateKey = Text.Key(row["stav"]);
        var state = stateKey.StartsWith("ukon") ? EmployeeState.Terminated : stateKey.StartsWith("rodic") ? EmployeeState.ParentalLeave : EmployeeState.Active;
        if (stateKey is not ("aktivni" or "") && state == EmployeeState.Active)
            log.Warn(Sources.Employees, id, $"Neznámý stav „{row["stav"]}“.", "Bráno jako aktivní.");
        if (hire > exportDate)
            log.Info(Sources.Employees, id, $"Nástup až {hire:yyyy-MM-dd} (po exportu).", "Označen jako nastupující; školení se plánují před nástupem.");
        if (state == EmployeeState.Active && end > exportDate)
            log.Info(Sources.Employees, id, $"Aktivní, ale s plánovaným ukončením {end:yyyy-MM-dd}.", "Plánovač ho nepřihlásí na termín po tomto datu.");
        if (state == EmployeeState.Terminated && end is null)
            log.Warn(Sources.Employees, id, "Ukončený zaměstnanec bez data ukončení.", "Do statistik se nezapočítává.");

        var rawDept = row.Trimmed("utvar");
        var dept = "";
        if (rawDept.Length > 0)
        {
            dept = departments[Text.Key(rawDept)];
            if (dept != rawDept) log.Info(Sources.Employees, id, $"Útvar „{rawDept}“ zapsán jinak než jinde.", $"Sjednoceno na „{dept}“.");
        }

        return new Draft
        {
            Id = id, FirstName = first, LastName = last, Email = email, Department = dept, Position = row.Trimmed("pozice"),
            ManagerEmail = row.Trimmed("vedouci_email").ToLowerInvariant(), HireDate = hire, TerminationDate = end, State = state,
        };
    }

    /// <summary>Chybějící útvar: z většiny podřízených (vedoucí), jinak z útvaru nadřízeného. Vždy s varováním k ověření.</summary>
    private static void InferMissingDepartments(List<Draft> drafts, IssueLog log)
    {
        foreach (var d in drafts.Where(d => d.Department.Length == 0))
        {
            var fromSubordinates = drafts.Where(x => x.ManagerId == d.Id && x.Department.Length > 0)
                .GroupBy(x => x.Department).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            var fromManager = drafts.FirstOrDefault(x => x.Id == d.ManagerId)?.Department;
            var inferred = fromSubordinates ?? (string.IsNullOrEmpty(fromManager) ? null : fromManager);
            d.Department = inferred ?? "Neuvedeno";
            d.DepartmentInferred = true;
            log.Warn(Sources.Employees, d.Id, "Chybí útvar.", inferred is null
                ? "Nelze odvodit, vedeno jako „Neuvedeno“."
                : $"Odvozeno „{inferred}“ ({(fromSubordinates is not null ? "z podřízených" : "z nadřízeného")}); ověřit.");
        }
    }

    [GeneratedRegex(@"^[^\s@,]+@[^\s@,]+\.[a-z]{2,}$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    private sealed class Draft
    {
        public string Id = "", FirstName = "", LastName = "", Email = "", Department = "", Position = "", ManagerEmail = "";
        public string? ManagerId;
        public bool DepartmentInferred;
        public DateOnly? HireDate, TerminationDate;
        public EmployeeState State;
    }
}
